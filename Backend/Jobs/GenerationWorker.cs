using Gifster.Backend.Providers;
using Gifster.Backend.Storage;

namespace Gifster.Backend.Jobs;

public sealed class GenerationWorker
{
  private readonly IJobStore jobStore;
  private readonly IGenerationProvider provider;
  private readonly IGenerationResultStore resultStore;

  public GenerationWorker(
    IJobStore jobStore,
    IGenerationProvider provider,
    IGenerationResultStore resultStore
  )
  {
    this.jobStore = jobStore;
    this.provider = provider;
    this.resultStore = resultStore;
  }

  public async Task ProcessJobAsync(string jobId, CancellationToken cancellationToken)
  {
    var job = await jobStore.GetAsync(jobId, cancellationToken).ConfigureAwait(false);
    if (job is null)
    {
      return;
    }

    var running = job with
    {
      Status = GenerationJobStatus.Running,
      UpdatedAt = DateTimeOffset.UtcNow
    };
    await jobStore.SaveAsync(running, cancellationToken).ConfigureAwait(false);

    try
    {
      var result = await provider.GetResultAsync(running, cancellationToken).ConfigureAwait(false);
      var stored = await resultStore
        .SaveAsync(running.Id, result, cancellationToken)
        .ConfigureAwait(false);

      await jobStore
        .SaveAsync(
          running with
          {
            Status = GenerationJobStatus.Succeeded,
            ResultBlobName = stored.BlobName,
            ResultContentType = stored.ContentType,
            UpdatedAt = DateTimeOffset.UtcNow
          },
          cancellationToken
        )
        .ConfigureAwait(false);
    }
    catch (Exception error) when (error is not OperationCanceledException)
    {
      await jobStore
        .SaveAsync(
          running with
          {
            Status = GenerationJobStatus.Failed,
            FailedMessage = error.Message,
            UpdatedAt = DateTimeOffset.UtcNow
          },
          cancellationToken
        )
        .ConfigureAwait(false);
    }
  }
}
