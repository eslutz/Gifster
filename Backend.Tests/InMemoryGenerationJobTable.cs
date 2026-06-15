using Gifster.Backend.Jobs;

namespace Gifster.Backend.Tests;

internal sealed class InMemoryGenerationJobTable : IGenerationJobTable
{
  private readonly Dictionary<string, GenerationJobTableEntity> rows = [];

  public Task UpsertAsync(GenerationJobTableEntity entity, CancellationToken cancellationToken)
  {
    rows[entity.RowKey] = entity;
    return Task.CompletedTask;
  }

  public Task<GenerationJobTableEntity?> GetAsync(string jobId, CancellationToken cancellationToken)
  {
    rows.TryGetValue(jobId, out var entity);
    return Task.FromResult(entity);
  }

  public Task<int> DeleteExpiredAsync(
    DateTimeOffset expiresBefore,
    int maxCount,
    CancellationToken cancellationToken
  )
  {
    var expiredKeys = rows
      .Values
      .Where(row => row.ExpiresAt <= expiresBefore)
      .OrderBy(row => row.ExpiresAt)
      .Take(maxCount)
      .Select(row => row.RowKey)
      .ToArray();

    foreach (var key in expiredKeys)
    {
      rows.Remove(key);
    }

    return Task.FromResult(expiredKeys.Length);
  }
}
