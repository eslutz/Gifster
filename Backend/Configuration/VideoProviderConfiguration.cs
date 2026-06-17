using GifForge.Backend.Providers;
using Microsoft.Extensions.Configuration;

namespace GifForge.Backend.Configuration;

public static class VideoProviderConfiguration
{
  private static readonly ProviderModelDefinition[] FalModels =
  [
    new(
      "FAL_WAN22_TEXT_TO_VIDEO",
      "fal-ai/wan/v2.2-a14b/text-to-video",
      VideoGenerationCapability.TextToVideo,
      true
    ),
    new(
      "FAL_WAN22_IMAGE_TO_VIDEO",
      "fal-ai/wan/v2.2-a14b/image-to-video",
      VideoGenerationCapability.ImageToVideo,
      true
    ),
    new(
      "FAL_WAN22_VIDEO_TO_VIDEO",
      "fal-ai/wan/v2.2-a14b/video-to-video",
      VideoGenerationCapability.VideoToVideo,
      true
    )
  ];

  private static readonly ProviderModelDefinition[] LumaModels =
  [
    new(
      "LUMA_RAY32_TEXT_TO_VIDEO",
      "ray-3.2",
      VideoGenerationCapability.TextToVideo,
      true
    ),
    new(
      "LUMA_RAY32_IMAGE_TO_VIDEO",
      "ray-3.2",
      VideoGenerationCapability.ImageToVideo,
      true
    ),
    new(
      "LUMA_RAY32_VIDEO_TO_VIDEO",
      "ray-3.2",
      VideoGenerationCapability.VideoToVideo,
      true
    )
  ];

  public static HttpVideoGenerationProviderOptions Fal(IConfiguration configuration) =>
    new(
      "fal.ai",
      configuration["GIFFORGE_FAL_SUBMIT_URL_TEMPLATE"] ?? "https://queue.fal.run/{modelId}",
      configuration["GIFFORGE_FAL_RESULT_URL_TEMPLATE"] ?? "https://queue.fal.run/{modelId}/requests/{providerJobId}",
      AuthorizationHeader("Key", RequiredSecret(configuration, "GIFFORGE_FAL_API_KEY", "fal.ai")),
      Models(configuration, FalModels)
    );

  public static HttpVideoGenerationProviderOptions Luma(IConfiguration configuration) =>
    new(
      "luma",
      configuration["GIFFORGE_LUMA_SUBMIT_URL_TEMPLATE"] ?? "https://api.lumalabs.ai/dream-machine/v1/generations",
      configuration["GIFFORGE_LUMA_RESULT_URL_TEMPLATE"] ?? "https://api.lumalabs.ai/dream-machine/v1/generations/{providerJobId}",
      AuthorizationHeader("Bearer", RequiredSecret(configuration, "GIFFORGE_LUMA_API_KEY", "luma")),
      Models(configuration, LumaModels)
    );

  public static bool IsEnabled(IConfiguration configuration, string providerName, string apiKeyName) =>
    configuration[$"GIFFORGE_{providerName}_ENABLED"] is { } value
      ? string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
      : !string.IsNullOrWhiteSpace(configuration[apiKeyName]);

  private static IReadOnlyList<VideoGenerationModel> Models(
    IConfiguration configuration,
    IReadOnlyList<ProviderModelDefinition> definitions
  ) =>
    definitions
      .Select(model => new VideoGenerationModel(
        model.Key,
        model.ModelId,
        model.Capability,
        RequiredCost(configuration, CostConfigurationKey(model.Key)),
        model.Enabled
      ))
      .ToArray();

  private static string CostConfigurationKey(string modelKey) =>
    $"GIFFORGE_MODEL_COST_USD_{modelKey}";

  private static decimal RequiredCost(IConfiguration configuration, string costKey)
  {
    var value = configuration[costKey];
    if (decimal.TryParse(value, out var cost) && cost >= 0)
    {
      return cost;
    }

    throw new InvalidOperationException(
      $"Provider model cost configuration {costKey} must be set to a non-negative decimal value in App Configuration."
    );
  }

  private static string RequiredSecret(IConfiguration configuration, string key, string providerName)
  {
    var value = configuration[key];
    if (!string.IsNullOrWhiteSpace(value))
    {
      return value;
    }

    throw new InvalidOperationException(
      $"Provider {providerName} is enabled but {key} is not configured. Store provider API keys in Azure Key Vault."
    );
  }

  private static string? AuthorizationHeader(string scheme, string? value) =>
    string.IsNullOrWhiteSpace(value) ? null : $"{scheme} {value}";

  private sealed record ProviderModelDefinition(
    string Key,
    string ModelId,
    VideoGenerationCapability Capability,
    bool Enabled
  );
}
