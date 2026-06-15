using Gifster.Backend.Jobs;
using Microsoft.Extensions.Logging;

namespace Gifster.Backend.Operations;

public interface IGenerationEventSink
{
  void Record(GenerationOperationalEvent generationEvent);
}

public sealed record GenerationOperationalEvent(
  string Name,
  string JobId,
  string Provider,
  string Mode,
  string Status,
  bool HasSourceImage,
  string CaptionMode,
  string? ResultContentType = null,
  string? FailureKind = null
)
{
  public static GenerationOperationalEvent FromJob(
    string name,
    GenerationJob job,
    string? resultContentType = null,
    string? failureKind = null
  ) =>
    new(
      name,
      job.Id,
      job.Provider,
      job.Request.Mode,
      job.Status.JsonValue(),
      job.Request.SourceImage is not null,
      job.Request.Caption?.Mode ?? "none",
      resultContentType,
      failureKind
    );
}

public sealed class LoggingGenerationEventSink : IGenerationEventSink
{
  private readonly ILogger<LoggingGenerationEventSink> logger;

  public LoggingGenerationEventSink(ILogger<LoggingGenerationEventSink> logger)
  {
    this.logger = logger;
  }

  public void Record(GenerationOperationalEvent generationEvent)
  {
    logger.LogInformation(
      "Generation event {GenerationEventName} for job {GenerationJobId}: provider={GenerationProvider} mode={GenerationMode} status={GenerationStatus} hasSourceImage={HasSourceImage} captionMode={CaptionMode} resultContentType={ResultContentType} failureKind={FailureKind}",
      generationEvent.Name,
      generationEvent.JobId,
      generationEvent.Provider,
      generationEvent.Mode,
      generationEvent.Status,
      generationEvent.HasSourceImage,
      generationEvent.CaptionMode,
      generationEvent.ResultContentType,
      generationEvent.FailureKind
    );
  }
}

public sealed class NoopGenerationEventSink : IGenerationEventSink
{
  public static NoopGenerationEventSink Instance { get; } = new();

  private NoopGenerationEventSink()
  {
  }

  public void Record(GenerationOperationalEvent generationEvent)
  {
  }
}
