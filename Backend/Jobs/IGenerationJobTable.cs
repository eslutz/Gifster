namespace Gifster.Backend.Jobs;

public interface IGenerationJobTable
{
  Task UpsertAsync(GenerationJobTableEntity entity, CancellationToken cancellationToken);
  Task<GenerationJobTableEntity?> GetAsync(string jobId, CancellationToken cancellationToken);
}
