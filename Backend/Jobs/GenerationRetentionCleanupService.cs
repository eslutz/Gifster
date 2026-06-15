using Gifster.Backend.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gifster.Backend.Jobs;

public sealed class GenerationRetentionCleanupService : BackgroundService
{
  private readonly IJobStore jobStore;
  private readonly GenerationRetentionOptions options;
  private readonly ILogger<GenerationRetentionCleanupService> logger;

  public GenerationRetentionCleanupService(
    IJobStore jobStore,
    GenerationRetentionOptions options,
    ILogger<GenerationRetentionCleanupService> logger
  )
  {
    this.jobStore = jobStore;
    this.options = options;
    this.logger = logger;
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    while (!stoppingToken.IsCancellationRequested)
    {
      await CleanupOnceAsync(stoppingToken).ConfigureAwait(false);
      await Task.Delay(options.CleanupInterval, stoppingToken).ConfigureAwait(false);
    }
  }

  internal async Task<int> CleanupOnceAsync(CancellationToken cancellationToken)
  {
    var deleted = await jobStore
      .DeleteExpiredAsync(DateTimeOffset.UtcNow, options.CleanupBatchSize, cancellationToken)
      .ConfigureAwait(false);

    if (deleted > 0)
    {
      logger.LogInformation("Deleted {DeletedJobCount} expired generation job rows.", deleted);
    }

    return deleted;
  }
}
