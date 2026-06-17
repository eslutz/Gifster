using Microsoft.Extensions.Configuration;

namespace GifForge.ProviderLogDrain.Function;

public sealed record ProviderDrainOptions(
  string FalDrainSecret,
  string? DataCollectionEndpoint = null,
  string? DataCollectionRuleId = null,
  string DataCollectionStreamName = "Custom-ProviderLogs"
)
{
  public static ProviderDrainOptions FromConfiguration(IConfiguration configuration)
  {
    var falDrainSecret = Required(configuration, "FAL_DRAIN_SECRET");
    if (falDrainSecret.Length < 64)
    {
      throw new InvalidOperationException("FAL_DRAIN_SECRET must be at least 64 characters.");
    }

    return new ProviderDrainOptions(
      falDrainSecret,
      Required(configuration, "AZURE_MONITOR_DCR_ENDPOINT"),
      Required(configuration, "AZURE_MONITOR_DCR_IMMUTABLE_ID"),
      configuration["AZURE_MONITOR_DCR_STREAM_NAME"] ?? "Custom-ProviderLogs"
    );
  }

  private static string Required(IConfiguration configuration, string key)
  {
    var value = configuration[key];
    if (string.IsNullOrWhiteSpace(value))
    {
      throw new InvalidOperationException($"{key} is required.");
    }

    return value.Trim();
  }
}
