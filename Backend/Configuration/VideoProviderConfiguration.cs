using GifForge.Backend.Providers;
using Microsoft.Extensions.Configuration;

namespace GifForge.Backend.Configuration;

public static class VideoProviderConfiguration
{
  private static readonly VideoGenerationModel[] FalModels =
  [
    new(
      "FAL_WAN22_TEXT_TO_VIDEO",
      "fal-ai/wan/v2.2-a14b/text-to-video",
      VideoGenerationCapability.TextToVideo,
      0.03m,
      0.03m,
      true
    ),
    new(
      "FAL_WAN22_IMAGE_TO_VIDEO",
      "fal-ai/wan/v2.2-a14b/image-to-video",
      VideoGenerationCapability.ImageToVideo,
      0.04m,
      0.04m,
      true
    ),
    new(
      "FAL_WAN22_VIDEO_TO_VIDEO",
      "fal-ai/wan/v2.2-a14b/video-to-video",
      VideoGenerationCapability.VideoToVideo,
      0.05m,
      0.05m,
      true
    )
  ];

  private static readonly VideoGenerationModel[] LumaModels =
  [
    new(
      "LUMA_RAY32_TEXT_TO_VIDEO",
      "ray-3.2",
      VideoGenerationCapability.TextToVideo,
      0.16m,
      0.16m,
      true
    ),
    new(
      "LUMA_RAY32_IMAGE_TO_VIDEO",
      "ray-3.2",
      VideoGenerationCapability.ImageToVideo,
      0.18m,
      0.18m,
      true
    ),
    new(
      "LUMA_RAY32_VIDEO_TO_VIDEO",
      "ray-3.2",
      VideoGenerationCapability.VideoToVideo,
      0.22m,
      0.22m,
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
    IReadOnlyList<VideoGenerationModel> defaults
  ) =>
    defaults
      .Select(model => model with
      {
        EstimatedCostUsd = Cost(configuration, model.CostConfigurationKey, model.DefaultEstimatedCostUsd)
      })
      .ToArray();

  private static decimal Cost(IConfiguration configuration, string costKey, decimal defaultCost) =>
    decimal.TryParse(configuration[costKey], out var cost) && cost >= 0 ? cost : defaultCost;

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
}
