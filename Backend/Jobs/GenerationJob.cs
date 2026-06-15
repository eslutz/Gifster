using Gifster.Backend.Models;
using Gifster.Backend.Providers;

namespace Gifster.Backend.Jobs;

public sealed record GenerationJob(
  string Id,
  GenerationRequest Request,
  string Provider,
  string ProviderJobId,
  GenerationJobStatus Status,
  DateTimeOffset CreatedAt,
  DateTimeOffset UpdatedAt,
  DateTimeOffset ExpiresAt,
  string? ResultBlobName = null,
  string? ResultContentType = null,
  string? FailedMessage = null
)
{
  public static readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(24);

  public static GenerationJob Create(
    GenerationRequest request,
    ProviderJob providerJob,
    TimeSpan? lifetime = null
  )
  {
    var now = DateTimeOffset.UtcNow;
    return new GenerationJob(
      Guid.NewGuid().ToString("D"),
      request,
      providerJob.Provider,
      providerJob.ProviderJobId,
      GenerationJobStatus.Queued,
      now,
      now,
      now.Add(lifetime ?? DefaultLifetime)
    );
  }

  public bool IsExpired(DateTimeOffset now) => ExpiresAt <= now;
}

public enum GenerationJobStatus
{
  Queued,
  Running,
  Succeeded,
  Failed
}

public static class GenerationJobStatusExtensions
{
  public static string JsonValue(this GenerationJobStatus status) =>
    status switch
    {
      GenerationJobStatus.Queued => "queued",
      GenerationJobStatus.Running => "running",
      GenerationJobStatus.Succeeded => "succeeded",
      GenerationJobStatus.Failed => "failed",
      _ => "failed"
    };

  public static GenerationJobStatus FromJsonValue(string? value) =>
    value?.Trim().ToLowerInvariant() switch
    {
      "queued" => GenerationJobStatus.Queued,
      "running" => GenerationJobStatus.Running,
      "succeeded" => GenerationJobStatus.Succeeded,
      "failed" => GenerationJobStatus.Failed,
      _ => GenerationJobStatus.Failed
    };
}
