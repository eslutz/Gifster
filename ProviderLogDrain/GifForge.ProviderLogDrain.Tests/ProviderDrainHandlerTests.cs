using System.Net;
using System.Security.Cryptography;
using System.Text;
using GifForge.ProviderLogDrain.Function;

namespace GifForge.ProviderLogDrain.Tests;

public sealed class ProviderDrainHandlerTests
{
  private const string Secret = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

  [Fact]
  public async Task HandleAsyncAcceptsSignedFalNdjsonAndNormalizesRecords()
  {
    var sink = new RecordingProviderLogIngestionSink();
    var handler = new ProviderDrainHandler(sink, new ProviderDrainOptions(Secret));
    var body = Encoding.UTF8.GetBytes("""
      {"timestamp":"2026-06-17T09:00:00Z","level":"info","message":"queued","job_id":"job_1","request_id":"req_1","app":"gifforge","revision":"rev_1","runner_id":"runner_1","labels":{"environment":"prod"}}
      {"time":"2026-06-17T09:00:01Z","severity":"warn","msg":"retry","providerJobId":"job_2","requestId":"req_2"}
      """);

    var result = await handler.HandleAsync(
      "fal",
      body,
      new Dictionary<string, string> { ["X-Fal-Signature"] = Signature(body) },
      CancellationToken.None
    );

    Assert.Equal(HttpStatusCode.Accepted, result.StatusCode);
    Assert.Equal(2, sink.Records.Count);
    Assert.Equal("fal", sink.Records[0].ProviderName);
    Assert.Equal("job_1", sink.Records[0].ProviderJobId);
    Assert.Equal("req_1", sink.Records[0].ProviderRequestId);
    Assert.Equal("gifforge", sink.Records[0].App);
    Assert.Equal("rev_1", sink.Records[0].Revision);
    Assert.Equal("runner_1", sink.Records[0].RunnerId);
    Assert.Contains("\"environment\":\"prod\"", sink.Records[0].LabelsJson);
    Assert.Equal("job_2", sink.Records[1].ProviderJobId);
    Assert.Equal("req_2", sink.Records[1].ProviderRequestId);
    Assert.Null(sink.Records[1].ParseError);
  }

  [Theory]
  [InlineData("")]
  [InlineData("bad-signature")]
  public async Task HandleAsyncRejectsMissingOrBadFalSignature(string signature)
  {
    var sink = new RecordingProviderLogIngestionSink();
    var handler = new ProviderDrainHandler(sink, new ProviderDrainOptions(Secret));
    var body = Encoding.UTF8.GetBytes("""{"message":"private"}""");
    var headers = string.IsNullOrEmpty(signature)
      ? new Dictionary<string, string>()
      : new Dictionary<string, string> { ["X-Fal-Signature"] = signature };

    var result = await handler.HandleAsync("fal", body, headers, CancellationToken.None);

    Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
    Assert.Empty(sink.Records);
    Assert.DoesNotContain(Secret, result.Body);
  }

  [Fact]
  public async Task HandleAsyncAcceptsInvalidJsonLineWithParseErrorAndRawLine()
  {
    var sink = new RecordingProviderLogIngestionSink();
    var handler = new ProviderDrainHandler(sink, new ProviderDrainOptions(Secret));
    var body = Encoding.UTF8.GetBytes("not-json");

    var result = await handler.HandleAsync(
      "fal",
      body,
      new Dictionary<string, string> { ["X-Fal-Signature"] = Signature(body) },
      CancellationToken.None
    );

    Assert.Equal(HttpStatusCode.Accepted, result.StatusCode);
    var record = Assert.Single(sink.Records);
    Assert.Equal("invalid_json", record.ParseError);
    Assert.Equal("not-json", record.RawLine);
  }

  [Fact]
  public async Task HandleAsyncRejectsUnsupportedProvidersWithoutIngesting()
  {
    var sink = new RecordingProviderLogIngestionSink();
    var handler = new ProviderDrainHandler(sink, new ProviderDrainOptions(Secret));

    var result = await handler.HandleAsync(
      "luma",
      Encoding.UTF8.GetBytes("""{"message":"future"}"""),
      new Dictionary<string, string>(),
      CancellationToken.None
    );

    Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
    Assert.Contains("Unsupported provider", result.Body);
    Assert.Empty(sink.Records);
  }

  [Fact]
  public async Task HandleAsyncReturnsBadGatewayWhenIngestionFails()
  {
    var sink = new RecordingProviderLogIngestionSink
    {
      Error = new InvalidOperationException("ingestion secret detail")
    };
    var handler = new ProviderDrainHandler(sink, new ProviderDrainOptions(Secret));
    var body = Encoding.UTF8.GetBytes("""{"message":"accepted but downstream fails"}""");

    var result = await handler.HandleAsync(
      "fal",
      body,
      new Dictionary<string, string> { ["X-Fal-Signature"] = Signature(body) },
      CancellationToken.None
    );

    Assert.Equal(HttpStatusCode.BadGateway, result.StatusCode);
    Assert.DoesNotContain("ingestion secret detail", result.Body);
  }

  private static string Signature(byte[] body)
  {
    var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), body);
    return Convert.ToHexString(signature).ToLowerInvariant();
  }

  private sealed class RecordingProviderLogIngestionSink : IProviderLogIngestionSink
  {
    public List<ProviderLogRecord> Records { get; } = [];

    public Exception? Error { get; init; }

    public Task IngestAsync(IReadOnlyList<ProviderLogRecord> records, CancellationToken cancellationToken)
    {
      if (Error is not null)
      {
        throw Error;
      }
      Records.AddRange(records);
      return Task.CompletedTask;
    }
  }
}
