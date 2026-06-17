using GifForge.Backend.Jobs;
using GifForge.Backend.Operations;
using GifForge.Backend.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GifForge.Backend.Tests;

public sealed class BackendStructuredLoggingTests
{
  [Fact]
  public void FromConfigurationDefaultsToLocalTestContextWhenNotRequired()
  {
    var configuration = new ConfigurationBuilder().Build();

    var context = BackendLogContext.FromConfiguration(configuration);

    Assert.Equal("local", context.EnvironmentName);
    Assert.Equal("backend-test", context.Component);
    Assert.Equal("GifForge.Backend", context.Service);
    Assert.False(string.IsNullOrWhiteSpace(context.Version));
  }

  [Fact]
  public void FromConfigurationRequiresEnvironmentAndComponentWhenEnabled()
  {
    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?>
      {
        ["GIFFORGE_REQUIRE_STRUCTURED_LOG_CONTEXT"] = "true"
      })
      .Build();

    var error = Assert.Throws<InvalidOperationException>(() => BackendLogContext.FromConfiguration(configuration));

    Assert.Contains("GIFFORGE_ENVIRONMENT_NAME", error.Message);
    Assert.Contains("GIFFORGE_LOG_COMPONENT", error.Message);
  }

  [Fact]
  public void LoggingGenerationEventSinkWritesRequiredStructuredFields()
  {
    var logger = new CapturingLogger<LoggingGenerationEventSink>();
    var context = new BackendLogContext("prod", "backend-api", "GifForge.Backend", "test-version");
    var sink = new LoggingGenerationEventSink(logger, context);
    var job = GenerationJob.Create(
      TestGenerationRequests.Valid(),
      new ProviderJob("fal.ai", "provider-job-1", "fal-model-1")
    );

    sink.Record(GenerationOperationalEvent.FromJob("generation.queued", job));

    var entry = Assert.Single(logger.Entries);
    AssertStructuredValue(entry.State, "GifForgeEnvironment", "prod");
    AssertStructuredValue(entry.State, "GifForgeComponent", "backend-api");
    AssertStructuredValue(entry.State, "GifForgeService", "GifForge.Backend");
    AssertStructuredValue(entry.State, "GifForgeVersion", "test-version");
    AssertStructuredValue(entry.State, "GenerationEventName", "generation.queued");
    AssertStructuredValue(entry.State, "GenerationJobId", job.Id);
    AssertStructuredValue(entry.State, "Provider", "fal.ai");
    AssertStructuredValue(entry.State, "ProviderJobId", "provider-job-1");
    AssertStructuredValue(entry.State, "GenerationMode", "text_to_gif");
    AssertStructuredValue(entry.State, "GenerationStatus", "queued");
    Assert.Contains("GifForgeEnvironment=prod", entry.FormattedMessage);
    Assert.Contains("GifForgeComponent=backend-api", entry.FormattedMessage);
    Assert.Contains("ProviderJobId=provider-job-1", entry.FormattedMessage);
  }

  private static void AssertStructuredValue(
    IReadOnlyList<KeyValuePair<string, object?>> state,
    string key,
    object expected
  )
  {
    var pair = Assert.Single(state, item => item.Key == key);
    Assert.Equal(expected, pair.Value);
  }

  private sealed class CapturingLogger<T> : ILogger<T>
  {
    public List<CapturedLogEntry> Entries { get; } = [];

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
      LogLevel logLevel,
      EventId eventId,
      TState state,
      Exception? exception,
      Func<TState, Exception?, string> formatter
    )
    {
      var structuredState = state as IReadOnlyList<KeyValuePair<string, object?>>
        ?? throw new InvalidOperationException("Expected structured logger state.");
      Entries.Add(new CapturedLogEntry(logLevel, structuredState, formatter(state, exception)));
    }
  }

  private sealed record CapturedLogEntry(
    LogLevel LogLevel,
    IReadOnlyList<KeyValuePair<string, object?>> State,
    string FormattedMessage
  );

  private sealed class NullScope : IDisposable
  {
    public static NullScope Instance { get; } = new();

    public void Dispose()
    {
    }
  }
}
