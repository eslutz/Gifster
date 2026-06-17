using GifForge.Backend.Jobs;
using Microsoft.Extensions.Logging;

namespace GifForge.Backend.Operations;

public interface IGenerationEventSink
{
  void Record(GenerationOperationalEvent generationEvent);
}

public sealed record GenerationOperationalEvent(
  string Name,
  string JobId,
  string Provider,
  string ProviderJobId,
  string Mode,
  string Status,
  bool HasSourceImage,
  bool HasSourceMedia,
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
      job.ProviderJobId,
      job.Request.Mode,
      job.Status.JsonValue(),
      job.Request.SourceImage is not null,
      job.Request.SourceMedia is not null,
      job.Request.Caption?.Mode ?? "none",
      resultContentType,
      failureKind
    );
}

public sealed class LoggingGenerationEventSink : IGenerationEventSink
{
  private readonly ILogger<LoggingGenerationEventSink> logger;
  private readonly BackendLogContext logContext;

  public LoggingGenerationEventSink(
    ILogger<LoggingGenerationEventSink> logger,
    BackendLogContext logContext
  )
  {
    this.logger = logger;
    this.logContext = logContext;
  }

  public void Record(GenerationOperationalEvent generationEvent)
  {
    logger.LogInformation(
      "Generation event {GenerationEventName} for job {GenerationJobId}: environment={GifForgeEnvironment} component={GifForgeComponent} service={GifForgeService} version={GifForgeVersion} provider={Provider} providerJobId={ProviderJobId} mode={GenerationMode} status={GenerationStatus} hasSourceImage={HasSourceImage} hasSourceMedia={HasSourceMedia} captionMode={CaptionMode} resultContentType={ResultContentType} failureKind={FailureKind}",
      generationEvent.Name,
      generationEvent.JobId,
      logContext.EnvironmentName,
      logContext.Component,
      logContext.Service,
      logContext.Version,
      generationEvent.Provider,
      generationEvent.ProviderJobId,
      generationEvent.Mode,
      generationEvent.Status,
      generationEvent.HasSourceImage,
      generationEvent.HasSourceMedia,
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
