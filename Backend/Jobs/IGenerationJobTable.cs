namespace Gifster.Backend.Jobs;

public interface IGenerationJobTable
{
  Task UpsertAsync(GenerationJobTableEntity entity, CancellationToken cancellationToken);
  Task<GenerationJobTableEntity?> GetAsync(string jobId, CancellationToken cancellationToken);
  Task<int> DeleteExpiredAsync(DateTimeOffset expiresBefore, int maxCount, CancellationToken cancellationToken);
}
