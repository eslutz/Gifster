using Gifster.Backend.Operations;
using Gifster.Backend.Providers;
using Gifster.Backend.Storage;

namespace Gifster.Backend.Jobs;

public sealed class GenerationWorker
{
  private readonly IJobStore jobStore;
  private readonly IGenerationProvider provider;
  private readonly IGenerationResultStore resultStore;
  private readonly IGenerationEventSink generationEvents;

  public GenerationWorker(
    IJobStore jobStore,
    IGenerationProvider provider,
    IGenerationResultStore resultStore,
    IGenerationEventSink? generationEvents = null
  )
  {
    this.jobStore = jobStore;
    this.provider = provider;
    this.resultStore = resultStore;
    this.generationEvents = generationEvents ?? NoopGenerationEventSink.Instance;
  }

  public async Task ProcessJobAsync(string jobId, CancellationToken cancellationToken)
  {
    var job = await jobStore.GetAsync(jobId, cancellationToken).ConfigureAwait(false);
    if (job is null)
    {
      return;
    }

    if (job.IsExpired(DateTimeOffset.UtcNow))
    {
      return;
    }

    var running = job with
    {
      Status = GenerationJobStatus.Running,
      UpdatedAt = DateTimeOffset.UtcNow
    };
    await jobStore.SaveAsync(running, cancellationToken).ConfigureAwait(false);
    generationEvents.Record(GenerationOperationalEvent.FromJob("generation.running", running));

    try
    {
      var result = await provider.GetResultAsync(running, cancellationToken).ConfigureAwait(false);
      var stored = await resultStore
        .SaveAsync(running.Id, result, cancellationToken)
        .ConfigureAwait(false);

      var succeeded = running with
      {
        Status = GenerationJobStatus.Succeeded,
        ResultBlobName = stored.BlobName,
        ResultContentType = stored.ContentType,
        UpdatedAt = DateTimeOffset.UtcNow
      };

      await jobStore.SaveAsync(succeeded, cancellationToken).ConfigureAwait(false);
      generationEvents.Record(GenerationOperationalEvent.FromJob(
        "generation.succeeded",
        succeeded,
        resultContentType: stored.ContentType
      ));
    }
    catch (GenerationPermanentFailureException error)
    {
      var failed = running with
      {
        Status = GenerationJobStatus.Failed,
        FailedMessage = error.Message,
        UpdatedAt = DateTimeOffset.UtcNow
      };

      await jobStore.SaveAsync(failed, cancellationToken).ConfigureAwait(false);
      generationEvents.Record(GenerationOperationalEvent.FromJob(
        "generation.failed",
        failed,
        failureKind: "permanent_provider_failure"
      ));
    }
  }
}
