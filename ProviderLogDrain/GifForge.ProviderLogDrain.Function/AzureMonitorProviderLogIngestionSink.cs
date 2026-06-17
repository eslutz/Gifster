using Azure.Core;
using Azure.Monitor.Ingestion;
using System.Text.Json;

namespace GifForge.ProviderLogDrain.Function;

public sealed class AzureMonitorProviderLogIngestionSink : IProviderLogIngestionSink
{
  private readonly LogsIngestionClient client;
  private readonly ProviderDrainOptions options;

  public AzureMonitorProviderLogIngestionSink(ProviderDrainOptions options, TokenCredential credential)
  {
    if (string.IsNullOrWhiteSpace(options.DataCollectionEndpoint))
    {
      throw new InvalidOperationException("AZURE_MONITOR_DCR_ENDPOINT is required.");
    }
    if (string.IsNullOrWhiteSpace(options.DataCollectionRuleId))
    {
      throw new InvalidOperationException("AZURE_MONITOR_DCR_IMMUTABLE_ID is required.");
    }

    this.options = options;
    client = new LogsIngestionClient(new Uri(options.DataCollectionEndpoint), credential);
  }

  public async Task IngestAsync(IReadOnlyList<ProviderLogRecord> records, CancellationToken cancellationToken)
  {
    if (records.Count == 0)
    {
      return;
    }

    var serializedRecords = records.Select(record =>
      BinaryData.FromBytes(JsonSerializer.SerializeToUtf8Bytes(record))
    );

    await client
      .UploadAsync(
        options.DataCollectionRuleId,
        options.DataCollectionStreamName,
        serializedRecords,
        cancellationToken: cancellationToken
      )
      .ConfigureAwait(false);
  }
}
