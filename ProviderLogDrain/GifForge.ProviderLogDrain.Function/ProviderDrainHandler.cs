using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GifForge.ProviderLogDrain.Function;

public sealed class ProviderDrainHandler
{
  private readonly IProviderLogIngestionSink ingestionSink;
  private readonly ProviderDrainOptions options;

  public ProviderDrainHandler(IProviderLogIngestionSink ingestionSink, ProviderDrainOptions options)
  {
    this.ingestionSink = ingestionSink;
    this.options = options;
  }

  public async Task<ProviderDrainResult> HandleAsync(
    string providerName,
    byte[] body,
    IReadOnlyDictionary<string, string> headers,
    CancellationToken cancellationToken
  )
  {
    var normalizedProviderName = providerName.Trim().ToLowerInvariant();
    if (normalizedProviderName != "fal")
    {
      return new ProviderDrainResult(HttpStatusCode.NotFound, $"Unsupported provider '{providerName}'.");
    }

    if (!IsValidFalSignature(body, headers))
    {
      return new ProviderDrainResult(HttpStatusCode.Unauthorized, "Invalid signature.");
    }

    var receivedAt = DateTimeOffset.UtcNow;
    var records = DecodeLines(body)
      .Select(line => NormalizeFalLine(line, receivedAt))
      .ToArray();

    if (records.Length == 0)
    {
      return new ProviderDrainResult(HttpStatusCode.Accepted, """{"accepted":0}""");
    }

    try
    {
      await ingestionSink.IngestAsync(records, cancellationToken).ConfigureAwait(false);
    }
    catch (Exception error) when (error is not OperationCanceledException)
    {
      return new ProviderDrainResult(HttpStatusCode.BadGateway, "Provider log ingestion failed.");
    }

    return new ProviderDrainResult(HttpStatusCode.Accepted, $$"""{"accepted":{{records.Length}}}""");
  }

  private bool IsValidFalSignature(byte[] body, IReadOnlyDictionary<string, string> headers)
  {
    var signature = HeaderValue(headers, "X-Fal-Signature");
    if (string.IsNullOrWhiteSpace(signature))
    {
      return false;
    }

    var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(options.FalDrainSecret), body);
    var expectedHex = Convert.ToHexString(expected).ToLowerInvariant();
    var normalizedSignature = signature.Trim();
    if (normalizedSignature.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
    {
      normalizedSignature = normalizedSignature["sha256=".Length..];
    }

    return CryptographicOperations.FixedTimeEquals(
      Encoding.ASCII.GetBytes(expectedHex),
      Encoding.ASCII.GetBytes(normalizedSignature.ToLowerInvariant())
    );
  }

  private static string? HeaderValue(IReadOnlyDictionary<string, string> headers, string name)
  {
    if (headers.TryGetValue(name, out var value))
    {
      return value;
    }

    return headers.FirstOrDefault(item => string.Equals(item.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
  }

  private static IEnumerable<string> DecodeLines(byte[] body)
  {
    return Encoding.UTF8.GetString(body)
      .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
      .Where(line => !string.IsNullOrWhiteSpace(line));
  }

  private static ProviderLogRecord NormalizeFalLine(string line, DateTimeOffset receivedAt)
  {
    try
    {
      using var document = JsonDocument.Parse(line);
      var root = document.RootElement;
      return new ProviderLogRecord(
        receivedAt,
        "fal",
        receivedAt,
        line,
        null,
        DateTimeValue(root, "timestamp", "time", "created_at", "createdAt"),
        StringValue(root, "level", "severity"),
        StringValue(root, "message", "msg"),
        StringValue(root, "providerJobId", "provider_job_id", "job_id", "jobId", "generation_id", "generationId"),
        StringValue(root, "providerRequestId", "provider_request_id", "request_id", "requestId", "id"),
        StringValue(root, "app", "app_id", "appId"),
        StringValue(root, "revision", "revision_id", "revisionId"),
        StringValue(root, "runner_id", "runnerId"),
        JsonValue(root, "labels")
      );
    }
    catch (JsonException)
    {
      return new ProviderLogRecord(
        receivedAt,
        "fal",
        receivedAt,
        line,
        "invalid_json",
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null
      );
    }
  }

  private static string? StringValue(JsonElement root, params string[] names)
  {
    foreach (var name in names)
    {
      if (root.TryGetProperty(name, out var value))
      {
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
      }
    }

    return null;
  }

  private static DateTimeOffset? DateTimeValue(JsonElement root, params string[] names)
  {
    var value = StringValue(root, names);
    return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
  }

  private static string? JsonValue(JsonElement root, string name)
  {
    return root.TryGetProperty(name, out var value) ? value.GetRawText() : null;
  }
}
