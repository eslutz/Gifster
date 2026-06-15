using System.Net;
using System.Text;
using System.Text.Json;
using Gifster.Backend.Jobs;
using Gifster.Backend.Models;
using Gifster.Backend.Providers;
using Gifster.Backend.Queueing;

namespace Gifster.Backend.Tests;

public sealed class BackendRouteTests
{
  [Fact]
  public async Task HealthEndpointUsesStandardHealthPath()
  {
    await using var app = GifsterBackendApp.Create(provider: new FakeFrameSequenceProvider());
    var baseAddress = await BackendRouteTestHost.StartAsync(app);
    using var client = new HttpClient { BaseAddress = baseAddress };

    var response = await client.GetAsync("/health");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var body = await response.Content.ReadAsStringAsync();
    Assert.Contains("\"ok\":true", body);
    Assert.DoesNotContain("\"Ok\"", body);
    var health = JsonSerializer.Deserialize<HealthResponse>(body, JsonOptions());
    Assert.NotNull(health);
    Assert.True(health.Ok);
    Assert.Equal("fake-frame-sequence", health.Provider);
    Assert.Equal("demo", health.Mode);
  }

  [Fact]
  public async Task CreateGenerationDispatchesQueuedJob()
  {
    var dispatcher = new RecordingGenerationJobDispatcher();
    await using var app = GifsterBackendApp.Create(
      provider: new FakeFrameSequenceProvider(),
      jobStore: new MemoryJobStore(),
      jobDispatcher: dispatcher
    );
    var baseAddress = await BackendRouteTestHost.StartAsync(app);
    using var client = new HttpClient { BaseAddress = baseAddress };
    var requestJson = JsonSerializer.Serialize(TestGenerationRequests.Valid(), JsonOptions());
    using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

    var response = await client.PostAsync("/v1/generations", content);

    Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    var jobId = Assert.Single(dispatcher.JobIds);
    Assert.False(string.IsNullOrWhiteSpace(jobId));
  }

  [Fact]
  public async Task CreateGenerationUsesForwardedHttpsHostInStatusUrl()
  {
    var dispatcher = new RecordingGenerationJobDispatcher();
    await using var app = GifsterBackendApp.Create(
      provider: new FakeFrameSequenceProvider(),
      jobStore: new MemoryJobStore(),
      jobDispatcher: dispatcher
    );
    var baseAddress = await BackendRouteTestHost.StartAsync(app);
    using var client = new HttpClient { BaseAddress = baseAddress };
    var requestJson = JsonSerializer.Serialize(TestGenerationRequests.Valid(), JsonOptions());
    using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/generations")
    {
      Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
    };
    request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");
    request.Headers.TryAddWithoutValidation("X-Forwarded-Host", "api.gifster.example");

    var response = await client.SendAsync(request);

    Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    var body = await response.Content.ReadAsStringAsync();
    var submission = JsonSerializer.Deserialize<JobSubmissionResponse>(body, JsonOptions());
    Assert.NotNull(submission);
    Assert.Equal(
      $"https://api.gifster.example/v1/generations/{submission.JobId}",
      submission.StatusUrl
    );
  }

  [Fact]
  public async Task HealthEndpointReportsConfiguredExternalProvider()
  {
    await using var app = GifsterBackendApp.Create(args: [
      "--GIFSTER_PROVIDER_ADAPTER=external-http",
      "--GIFSTER_EXTERNAL_PROVIDER_NAME=test-provider",
      "--GIFSTER_EXTERNAL_PROVIDER_SUBMIT_URL=https://provider.example.test/jobs",
      "--GIFSTER_EXTERNAL_PROVIDER_RESULT_URL_TEMPLATE=https://provider.example.test/jobs/{providerJobId}/result"
    ]);
    var baseAddress = await BackendRouteTestHost.StartAsync(app);
    using var client = new HttpClient { BaseAddress = baseAddress };

    var response = await client.GetAsync("/health");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var body = await response.Content.ReadAsStringAsync();
    Assert.Contains("\"provider\":\"test-provider\"", body);
  }

  private static JsonSerializerOptions JsonOptions() =>
    new(JsonSerializerDefaults.Web);
}

internal sealed class RecordingGenerationJobDispatcher : IGenerationJobDispatcher
{
  public List<string> JobIds { get; } = [];

  public Task DispatchAsync(GenerationJob job, CancellationToken cancellationToken)
  {
    JobIds.Add(job.Id);
    return Task.CompletedTask;
  }
}
