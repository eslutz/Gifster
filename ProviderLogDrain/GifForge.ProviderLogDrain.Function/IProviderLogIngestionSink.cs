namespace GifForge.ProviderLogDrain.Function;

public interface IProviderLogIngestionSink
{
  Task IngestAsync(IReadOnlyList<ProviderLogRecord> records, CancellationToken cancellationToken);
}
