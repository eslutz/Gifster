using Microsoft.Extensions.Configuration;

namespace Gifster.Backend.Configuration;

public sealed record GenerationRetentionOptions(
  TimeSpan JobLifetime,
  TimeSpan CleanupInterval,
  int CleanupBatchSize,
  bool CleanupEnabled
)
{
  public static GenerationRetentionOptions Default { get; } =
    new(TimeSpan.FromHours(24), TimeSpan.FromHours(6), 100, false);

  public static GenerationRetentionOptions FromConfiguration(IConfiguration configuration) =>
    new(
      PositiveHours(configuration["GIFSTER_GENERATION_JOB_RETENTION_HOURS"], Default.JobLifetime),
      PositiveMinutes(configuration["GIFSTER_RETENTION_CLEANUP_INTERVAL_MINUTES"], Default.CleanupInterval),
      PositiveInt(configuration["GIFSTER_RETENTION_CLEANUP_BATCH_SIZE"], Default.CleanupBatchSize),
      string.Equals(configuration["GIFSTER_RETENTION_CLEANUP_ENABLED"], "true", StringComparison.OrdinalIgnoreCase)
    );

  private static TimeSpan PositiveHours(string? value, TimeSpan fallback) =>
    int.TryParse(value, out var hours) && hours > 0 ? TimeSpan.FromHours(hours) : fallback;

  private static TimeSpan PositiveMinutes(string? value, TimeSpan fallback) =>
    int.TryParse(value, out var minutes) && minutes > 0 ? TimeSpan.FromMinutes(minutes) : fallback;

  private static int PositiveInt(string? value, int fallback) =>
    int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
}
