using Gifster.Backend.Jobs;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gifster.Backend.Queueing;

public sealed class GenerationQueueWorkerService : BackgroundService
{
  private static readonly TimeSpan EmptyQueueDelay = TimeSpan.FromSeconds(2);

  private readonly IGenerationJobQueueReader queueReader;
  private readonly GenerationWorker worker;
  private readonly ILogger<GenerationQueueWorkerService> logger;

  public GenerationQueueWorkerService(
    IGenerationJobQueueReader queueReader,
    GenerationWorker worker,
    ILogger<GenerationQueueWorkerService> logger
  )
  {
    this.queueReader = queueReader;
    this.worker = worker;
    this.logger = logger;
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    while (!stoppingToken.IsCancellationRequested)
    {
      var queuedJob = await queueReader.DequeueAsync(stoppingToken).ConfigureAwait(false);
      if (queuedJob is null)
      {
        await Task.Delay(EmptyQueueDelay, stoppingToken).ConfigureAwait(false);
        continue;
      }

      try
      {
        await worker.ProcessJobAsync(queuedJob.JobId, stoppingToken).ConfigureAwait(false);
        await queueReader.CompleteAsync(queuedJob, stoppingToken).ConfigureAwait(false);
      }
      catch (Exception error) when (error is not OperationCanceledException)
      {
        logger.LogError(
          error,
          "Generation queue job {JobId} failed and will be retried.",
          queuedJob.JobId
        );
        await Task.Delay(EmptyQueueDelay, stoppingToken).ConfigureAwait(false);
      }
    }
  }
}
