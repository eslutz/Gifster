using GifForge.Backend.Operations;
using GifForge.Backend.Providers;
using GifForge.Backend.Security;
using GifForge.Backend.Storage;

namespace GifForge.Backend.Jobs;

public sealed class GenerationWorker
{
  private readonly IJobStore jobStore;
  private readonly IGenerationProvider provider;
  private readonly IGenerationResultStore resultStore;
  private readonly IGenerationEventSink generationEvents;
  private readonly AccountSecurityService? accountSecurity;

  public GenerationWorker(
    IJobStore jobStore,
    IGenerationProvider provider,
    IGenerationResultStore resultStore,
    IGenerationEventSink? generationEvents = null,
    AccountSecurityService? accountSecurity = null
  )
  {
    this.jobStore = jobStore;
    this.provider = provider;
    this.resultStore = resultStore;
    this.generationEvents = generationEvents ?? NoopGenerationEventSink.Instance;
    this.accountSecurity = accountSecurity;
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
      if (accountSecurity is not null)
      {
        await accountSecurity.ReleaseGenerationCreditAsync(job.Id, "job_expired", cancellationToken)
          .ConfigureAwait(false);
      }
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
      if (accountSecurity is not null)
      {
        await accountSecurity.CaptureGenerationCreditAsync(succeeded.Id, cancellationToken)
          .ConfigureAwait(false);
      }
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
      if (accountSecurity is not null)
      {
        await accountSecurity.ReleaseGenerationCreditAsync(failed.Id, "permanent_provider_failure", cancellationToken)
          .ConfigureAwait(false);
      }
      generationEvents.Record(GenerationOperationalEvent.FromJob(
        "generation.failed",
        failed,
        failureKind: "permanent_provider_failure"
      ));
    }
  }
}
