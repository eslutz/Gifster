using Gifster.Backend.Models;
using Gifster.Backend.Providers;

namespace Gifster.Backend.Jobs;

public sealed class TableGenerationJobStore : IJobStore
{
  private readonly IGenerationJobTable table;

  public TableGenerationJobStore(IGenerationJobTable table)
  {
    this.table = table;
  }

  public async Task<GenerationJob> CreateAsync(
    GenerationRequest request,
    ProviderJob providerJob,
    CancellationToken cancellationToken
  )
  {
    var job = GenerationJob.Create(request, providerJob);
    await SaveAsync(job, cancellationToken).ConfigureAwait(false);
    return job;
  }

  public async Task<GenerationJob?> GetAsync(string id, CancellationToken cancellationToken)
  {
    var entity = await table.GetAsync(id, cancellationToken).ConfigureAwait(false);
    return entity?.ToJob();
  }

  public Task SaveAsync(GenerationJob job, CancellationToken cancellationToken) =>
    table.UpsertAsync(GenerationJobTableEntity.FromJob(job), cancellationToken);

  public GenerationJobStatus StatusFor(GenerationJob job) => job.Status;
}
