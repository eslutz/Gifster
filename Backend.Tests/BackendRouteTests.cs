using System.Net;
using System.Text;
using System.Text.Json;
using Gifster.Backend.Jobs;
using Gifster.Backend.Operations;
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
    var body = await response.Content.ReadAsStringAsync();
    var submission = JsonSerializer.Deserialize<JobSubmissionResponse>(body, JsonOptions());
    Assert.NotNull(submission);
    Assert.True(submission.ExpiresAt > DateTimeOffset.UtcNow);
    var jobId = Assert.Single(dispatcher.JobIds);
    Assert.False(string.IsNullOrWhiteSpace(jobId));
  }

  [Fact]
  public async Task CreateGenerationRecordsSanitizedQueuedEvent()
  {
    const string secretPrompt = "SECRET_PROMPT_TEXT";
    const string secretImagePayload = "SECRET_IMAGE_PAYLOAD";
    var dispatcher = new RecordingGenerationJobDispatcher();
    var eventSink = new RecordingGenerationEventSink();
    await using var app = GifsterBackendApp.Create(
      provider: new FakeFrameSequenceProvider(),
      jobStore: new MemoryJobStore(),
      jobDispatcher: dispatcher,
      generationEventSink: eventSink
    );
    var baseAddress = await BackendRouteTestHost.StartAsync(app);
    using var client = new HttpClient { BaseAddress = baseAddress };
    var request = TestGenerationRequests.Valid(secretPrompt) with
    {
      Mode = "image_to_gif",
      SourceImage = new SourceImageRequest(
        Convert.ToBase64String(Encoding.UTF8.GetBytes(secretImagePayload)),
        "image/jpeg",
        640,
        480
      ),
      Caption = new CaptionRequest("userText", "private caption")
    };
    var requestJson = JsonSerializer.Serialize(request, JsonOptions());
    using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

    var response = await client.PostAsync("/v1/generations", content);

    Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    var queued = Assert.Single(eventSink.Events);
    Assert.Equal("generation.queued", queued.Name);
    Assert.Equal("fake-frame-sequence", queued.Provider);
    Assert.Equal("image_to_gif", queued.Mode);
    Assert.True(queued.HasSourceImage);
    Assert.Equal("userText", queued.CaptionMode);
    Assert.DoesNotContain(secretPrompt, eventSink.SerializedEvents);
    Assert.DoesNotContain(secretImagePayload, eventSink.SerializedEvents);
    Assert.DoesNotContain("private caption", eventSink.SerializedEvents);
  }

  [Fact]
  public async Task CreateGenerationStoresSanitizedJobRequest()
  {
    const string originalPrompt = "raw original prompt that should not be stored";
    const string sourceImagePayload = "source image bytes that should not be stored";
    var dispatcher = new RecordingGenerationJobDispatcher();
    var jobStore = new MemoryJobStore();
    var provider = new RecordingGenerationProvider();
    await using var app = GifsterBackendApp.Create(
      provider: provider,
      jobStore: jobStore,
      jobDispatcher: dispatcher
    );
    var baseAddress = await BackendRouteTestHost.StartAsync(app);
    using var client = new HttpClient { BaseAddress = baseAddress };
    var request = TestGenerationRequests.Valid(originalPrompt) with
    {
      Mode = "image_to_gif",
      CleanedPrompt = "clean prompt",
      ExpandedPrompt = "Create a short looping animation of the clean prompt.",
      SourceImage = new SourceImageRequest(
        Convert.ToBase64String(Encoding.UTF8.GetBytes(sourceImagePayload)),
        "image/jpeg",
        640,
        480
      ),
      SourceImageContext = new SourceImageContextRequest(
        640,
        480,
        "landscape",
        "4:3",
        "User-selected landscape JPEG source image, 640x480, aspect 4:3."
      ),
      Caption = new CaptionRequest("userText", "private caption")
    };
    var requestJson = JsonSerializer.Serialize(request, JsonOptions());
    using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

    var response = await client.PostAsync("/v1/generations", content);

    Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    Assert.NotNull(provider.SubmittedRequest);
    Assert.Equal(originalPrompt, provider.SubmittedRequest.OriginalPrompt);
    Assert.Equal("private caption", provider.SubmittedRequest.Caption?.Text);
    Assert.Equal(
      Convert.ToBase64String(Encoding.UTF8.GetBytes(sourceImagePayload)),
      provider.SubmittedRequest.SourceImage?.DataBase64
    );
    var jobId = Assert.Single(dispatcher.JobIds);
    var stored = await jobStore.GetAsync(jobId, CancellationToken.None);
    Assert.NotNull(stored);
    Assert.Null(stored.Request.OriginalPrompt);
    Assert.Equal("clean prompt", stored.Request.CleanedPrompt);
    Assert.Equal("userText", stored.Request.Caption?.Mode);
    Assert.Null(stored.Request.Caption?.Text);
    Assert.NotNull(stored.Request.SourceImage);
    Assert.Equal(string.Empty, stored.Request.SourceImage.DataBase64);
    Assert.Equal(640, stored.Request.SourceImage.Width);
    Assert.Equal(480, stored.Request.SourceImage.Height);
    Assert.Equal("landscape", stored.Request.SourceImageContext?.Orientation);
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
    Assert.True(submission.ExpiresAt > DateTimeOffset.UtcNow);
  }

  [Fact]
  public async Task GetGenerationStatusReturnsGoneForExpiredJobs()
  {
    await using var app = GifsterBackendApp.Create(
      provider: new FakeFrameSequenceProvider(),
      jobStore: new MemoryJobStore(TimeSpan.FromMilliseconds(-1))
    );
    var baseAddress = await BackendRouteTestHost.StartAsync(app);
    using var client = new HttpClient { BaseAddress = baseAddress };
    var requestJson = JsonSerializer.Serialize(TestGenerationRequests.Valid(), JsonOptions());
    using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
    var createResponse = await client.PostAsync("/v1/generations", content);
    var createBody = await createResponse.Content.ReadAsStringAsync();
    var submission = JsonSerializer.Deserialize<JobSubmissionResponse>(createBody, JsonOptions());
    Assert.NotNull(submission);

    var statusResponse = await client.GetAsync(submission.StatusUrl);

    Assert.Equal(HttpStatusCode.Gone, statusResponse.StatusCode);
    var statusBody = await statusResponse.Content.ReadAsStringAsync();
    Assert.Contains("Generation job has expired.", statusBody);
  }

  [Fact]
  public async Task GetGenerationResultReturnsGoneForExpiredJobs()
  {
    await using var app = GifsterBackendApp.Create(
      provider: new FakeFrameSequenceProvider(),
      jobStore: new MemoryJobStore(TimeSpan.FromMilliseconds(-1))
    );
    var baseAddress = await BackendRouteTestHost.StartAsync(app);
    using var client = new HttpClient { BaseAddress = baseAddress };
    var requestJson = JsonSerializer.Serialize(TestGenerationRequests.Valid(), JsonOptions());
    using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
    var createResponse = await client.PostAsync("/v1/generations", content);
    var createBody = await createResponse.Content.ReadAsStringAsync();
    var submission = JsonSerializer.Deserialize<JobSubmissionResponse>(createBody, JsonOptions());
    Assert.NotNull(submission);

    var resultResponse = await client.GetAsync($"/v1/generations/{submission.JobId}/result");

    Assert.Equal(HttpStatusCode.Gone, resultResponse.StatusCode);
    var resultBody = await resultResponse.Content.ReadAsStringAsync();
    Assert.Contains("Generation result has expired.", resultBody);
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

internal sealed class RecordingGenerationProvider : IGenerationProvider
{
  public string Name => "recording-provider";

  public GenerationRequest? SubmittedRequest { get; private set; }

  public Task<ProviderJob> SubmitGenerationAsync(GenerationRequest request, CancellationToken cancellationToken)
  {
    SubmittedRequest = request;
    return Task.FromResult(new ProviderJob(Name, "recording-provider-job"));
  }

  public Task<GeneratedMotionResult> GetResultAsync(GenerationJob job, CancellationToken cancellationToken) =>
    throw new NotSupportedException();
}

internal sealed class RecordingGenerationEventSink : IGenerationEventSink
{
  public List<GenerationOperationalEvent> Events { get; } = [];

  public string SerializedEvents => string.Join('\n', Events.Select(item => item.ToString()));

  public void Record(GenerationOperationalEvent generationEvent)
  {
    Events.Add(generationEvent);
  }
}
