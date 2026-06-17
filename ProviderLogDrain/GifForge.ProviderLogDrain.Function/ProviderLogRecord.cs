namespace GifForge.ProviderLogDrain.Function;

public sealed record ProviderLogRecord(
  DateTimeOffset TimeGenerated,
  string ProviderName,
  DateTimeOffset ReceivedAt,
  string RawLine,
  string? ParseError,
  DateTimeOffset? ProviderTimestamp,
  string? Level,
  string? Message,
  string? ProviderJobId,
  string? ProviderRequestId,
  string? App,
  string? Revision,
  string? RunnerId,
  string? LabelsJson
);
