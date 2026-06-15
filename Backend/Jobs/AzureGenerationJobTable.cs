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
}
