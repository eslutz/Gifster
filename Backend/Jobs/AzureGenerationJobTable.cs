using Azure;
using Azure.Data.Tables;

namespace Gifster.Backend.Jobs;

public sealed class AzureGenerationJobTable : IGenerationJobTable
{
  private readonly TableClient client;

  public AzureGenerationJobTable(TableClient client)
  {
    this.client = client;
  }

  public async Task UpsertAsync(GenerationJobTableEntity entity, CancellationToken cancellationToken)
  {
    await client.UpsertEntityAsync(entity.ToTableEntity(), TableUpdateMode.Replace, cancellationToken)
      .ConfigureAwait(false);
  }

  public async Task<GenerationJobTableEntity?> GetAsync(string jobId, CancellationToken cancellationToken)
  {
    try
    {
      var response = await client
        .GetEntityAsync<TableEntity>(
          GenerationJobTableEntity.JobPartitionKey,
          jobId,
          cancellationToken: cancellationToken
        )
        .ConfigureAwait(false);

      return GenerationJobTableEntity.FromTableEntity(response.Value);
    }
    catch (RequestFailedException error) when (error.Status == StatusCodes.Status404NotFound)
    {
      return null;
    }
  }

  public async Task<int> DeleteExpiredAsync(
    DateTimeOffset expiresBefore,
    int maxCount,
    CancellationToken cancellationToken
  )
  {
    if (maxCount <= 0)
    {
      return 0;
    }

    var deleted = 0;
    var filter = TableClient.CreateQueryFilter(
      $"PartitionKey eq {GenerationJobTableEntity.JobPartitionKey} and ExpiresAt le {expiresBefore}"
    );

    await foreach (var entity in client
      .QueryAsync<TableEntity>(filter, maxPerPage: Math.Min(maxCount, 100), cancellationToken: cancellationToken)
      .ConfigureAwait(false))
    {
      if (deleted >= maxCount)
      {
        break;
      }

      await client
        .DeleteEntityAsync(entity.PartitionKey, entity.RowKey, entity.ETag, cancellationToken)
        .ConfigureAwait(false);
      deleted++;
    }

    return deleted;
  }
}
