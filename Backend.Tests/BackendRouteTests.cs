using System.Net;
using System.Text;
using System.Text.Json;
using GifForge.Backend.Jobs;
using GifForge.Backend.Operations;
using GifForge.Backend.Models;
using GifForge.Backend.Providers;
using GifForge.Backend.Queueing;

namespace GifForge.Backend.Tests;

public sealed class BackendRouteTests
{
  [Fact]
  public async Task HealthEndpointUsesStandardHealthPath()
  {
    await using var app = GifForgeBackendApp.Create(provider: new FakeFrameSequenceProvider());
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
    await using var app = GifForgeBackendApp.Create(
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
    await using var app = GifForgeBackendApp.Create(
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
    Assert.False(string.IsNullOrWhiteSpace(queued.ProviderJobId));
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
    await using var app = GifForgeBackendApp.Create(
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
  public async Task CreateGenerationDoesNotPersistRawSourceMediaOrServerRetryBlob()
  {
    const string sourceMediaPayload = "source media bytes that should stay out of table state";
    var dispatcher = new RecordingGenerationJobDispatcher();
    var jobStore = new MemoryJobStore();
    var provider = new RecordingGenerationProvider();
    await using var app = GifForgeBackendApp.Create(
      provider: provider,
      jobStore: jobStore,
      jobDispatcher: dispatcher
    );
    var baseAddress = await BackendRouteTestHost.StartAsync(app);
    using var client = new HttpClient { BaseAddress = baseAddress };
    var request = TestGenerationRequests.Valid("make this move") with
    {
      Mode = "video_to_gif",
      SourceMedia = new SourceMediaRequest(
        Convert.ToBase64String(Encoding.UTF8.GetBytes(sourceMediaPayload)),
        "video/quicktime",
        "IMG_0001.MOV",
        "livePhotoPairedVideo",
        "live-photo-1"
      )
    };
    var requestJson = JsonSerializer.Serialize(request, JsonOptions());
    using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

    var response = await client.PostAsync("/v1/generations", content);

    Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    Assert.NotNull(provider.SubmittedRequest?.SourceMedia);
    Assert.Equal(
      Convert.ToBase64String(Encoding.UTF8.GetBytes(sourceMediaPayload)),
      provider.SubmittedRequest.SourceMedia.DataBase64
    );
    var jobId = Assert.Single(dispatcher.JobIds);
    var stored = await jobStore.GetAsync(jobId, CancellationToken.None);
    Assert.NotNull(stored);
    Assert.Equal(string.Empty, stored.Request.SourceMedia?.DataBase64);
    Assert.Equal("video/quicktime", stored.Request.SourceMedia?.MimeType);
  }

  [Fact]
  public async Task CreateGenerationReturnsUnprocessableEntityForPermanentProviderSubmissionFailure()
  {
    var dispatcher = new RecordingGenerationJobDispatcher();
    await using var app = GifForgeBackendApp.Create(
      provider: new SubmitFailureProvider(new GenerationPermanentFailureException("provider secret rejection detail")),
      jobStore: new MemoryJobStore(),
      jobDispatcher: dispatcher
    );
    var baseAddress = await BackendRouteTestHost.StartAsync(app);
    using var client = new HttpClient { BaseAddress = baseAddress };
    var requestJson = JsonSerializer.Serialize(TestGenerationRequests.Valid(), JsonOptions());
    using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

    var response = await client.PostAsync("/v1/generations", content);

    Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    Assert.Empty(dispatcher.JobIds);
    var body = await response.Content.ReadAsStringAsync();
    Assert.Contains("Generation provider rejected the request.", body);
    Assert.DoesNotContain("provider secret rejection detail", body);
  }

  [Fact]
  public async Task CreateGenerationReturnsServiceUnavailableForTransientProviderSubmissionFailure()
  {
    var dispatcher = new RecordingGenerationJobDispatcher();
    await using var app = GifForgeBackendApp.Create(
      provider: new SubmitFailureProvider(new HttpRequestException("provider unavailable secret detail")),
      jobStore: new MemoryJobStore(),
      jobDispatcher: dispatcher
    );
    var baseAddress = await BackendRouteTestHost.StartAsync(app);
    using var client = new HttpClient { BaseAddress = baseAddress };
    var requestJson = JsonSerializer.Serialize(TestGenerationRequests.Valid(), JsonOptions());
    using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

    var response = await client.PostAsync("/v1/generations", content);

    Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    Assert.Empty(dispatcher.JobIds);
    var body = await response.Content.ReadAsStringAsync();
    Assert.Contains("Generation provider is temporarily unavailable.", body);
    Assert.DoesNotContain("provider unavailable secret detail", body);
  }

  [Fact]
  public async Task CreateGenerationUsesForwardedHttpsHostInStatusUrl()
  {
    var dispatcher = new RecordingGenerationJobDispatcher();
    await using var app = GifForgeBackendApp.Create(
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
    request.Headers.TryAddWithoutValidation("X-Forwarded-Host", "api.gifforge.example");

    var response = await client.SendAsync(request);

    Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    var body = await response.Content.ReadAsStringAsync();
    var submission = JsonSerializer.Deserialize<JobSubmissionResponse>(body, JsonOptions());
    Assert.NotNull(submission);
    Assert.Equal(
      $"https://api.gifforge.example/v1/generations/{submission.JobId}",
      submission.StatusUrl
    );
    Assert.True(submission.ExpiresAt > DateTimeOffset.UtcNow);
  }

  [Fact]
  public async Task GetGenerationStatusReturnsGoneForExpiredJobs()
  {
    await using var app = GifForgeBackendApp.Create(
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
    await using var app = GifForgeBackendApp.Create(
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
  public async Task FailedGenerationStatusIncludesClientRetryMetadata()
  {
    var jobStore = new MemoryJobStore();
    var failedJob = GenerationJob.Create(
      TestGenerationRequests.Valid(),
      new ProviderJob("fal.ai", "provider-job-1", "fal-wan-text"),
      TimeSpan.FromHours(1)
    ) with
    {
      Status = GenerationJobStatus.Failed,
      FailedMessage = "Generation provider reported failure."
    };
    await jobStore.SaveAsync(failedJob, CancellationToken.None);
    await using var app = GifForgeBackendApp.Create(
      provider: new FakeFrameSequenceProvider(),
      jobStore: jobStore
    );
    var baseAddress = await BackendRouteTestHost.StartAsync(app);
    using var client = new HttpClient { BaseAddress = baseAddress };

    var response = await client.GetAsync($"/v1/generations/{failedJob.Id}");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var body = await response.Content.ReadAsStringAsync();
    var status = JsonSerializer.Deserialize<JobStatusResponse>(body, JsonOptions());
    Assert.NotNull(status);
    Assert.Equal("failed", status.Status);
    Assert.True(status.RetryAvailable);
    Assert.Equal("provider_failed", status.RetryReason);
    Assert.Equal(failedJob.Id, status.RetryOfJobId);
  }

  [Fact]
  public async Task CreateGenerationRetryUsesPreviousAttemptMetadataWithoutStoredMedia()
  {
    var dispatcher = new RecordingGenerationJobDispatcher();
    var jobStore = new MemoryJobStore();
    var provider = new RecordingRetryGenerationProvider();
    var failedJob = GenerationJob.Create(
      TestGenerationRequests.Valid(),
      new ProviderJob("fal.ai", "provider-job-1", "fal-wan-text"),
      TimeSpan.FromHours(1)
    ) with
    {
      Status = GenerationJobStatus.Failed,
      FailedMessage = "Generation provider reported failure."
    };
    await jobStore.SaveAsync(failedJob, CancellationToken.None);
    await using var app = GifForgeBackendApp.Create(
      provider: provider,
      jobStore: jobStore,
      jobDispatcher: dispatcher
    );
    var baseAddress = await BackendRouteTestHost.StartAsync(app);
    using var client = new HttpClient { BaseAddress = baseAddress };
    var request = TestGenerationRequests.Valid("retry this") with
    {
      RetryOfJobId = failedJob.Id,
      SourceMedia = TestSourceMedia.Mp4()
    };
    var requestJson = JsonSerializer.Serialize(request, JsonOptions());
    using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

    var response = await client.PostAsync("/v1/generations", content);

    Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    Assert.Equal(failedJob.Id, provider.RetryOfJobId);
    Assert.Contains("fal.ai", provider.AttemptedProviders);
    Assert.Contains("fal-wan-text", provider.AttemptedModelIds);
    Assert.Equal(TestSourceMedia.Mp4().DataBase64, provider.SubmittedRequest?.SourceMedia?.DataBase64);
    var newJobId = Assert.Single(dispatcher.JobIds);
    var stored = await jobStore.GetAsync(newJobId, CancellationToken.None);
    Assert.NotNull(stored);
    Assert.Equal(string.Empty, stored.Request.SourceMedia?.DataBase64);
  }

  [Fact]
  public async Task HealthEndpointReportsRoutedVideoProviderByDefault()
  {
    await using var app = GifForgeBackendApp.Create(args: [
      "--GIFFORGE_FAL_API_KEY=fal-test-key",
      "--GIFFORGE_LUMA_API_KEY=luma-test-key",
      .. ProviderCostArgs()
    ]);
    var baseAddress = await BackendRouteTestHost.StartAsync(app);
    using var client = new HttpClient { BaseAddress = baseAddress };

    var response = await client.GetAsync("/health");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var body = await response.Content.ReadAsStringAsync();
    Assert.Contains("\"provider\":\"routed-video\"", body);
    Assert.Contains("\"mode\":\"video\"", body);
    Assert.DoesNotContain("\"mode\":\"demo\"", body);
  }

  [Fact]
  public async Task HealthEndpointAllowsOnlyConfiguredProvidersByDefault()
  {
    await using var app = GifForgeBackendApp.Create(args: [
      "--GIFFORGE_FAL_API_KEY=fal-test-key",
      .. FalCostArgs()
    ]);
    var baseAddress = await BackendRouteTestHost.StartAsync(app);
    using var client = new HttpClient { BaseAddress = baseAddress };

    var response = await client.GetAsync("/health");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var body = await response.Content.ReadAsStringAsync();
    Assert.Contains("\"provider\":\"routed-video\"", body);
    Assert.Contains("\"mode\":\"video\"", body);
  }

  [Fact]
  public async Task ProviderCallbackRequiresConfiguredSecret()
  {
    await using var app = GifForgeBackendApp.Create(provider: new FakeFrameSequenceProvider());
    var baseAddress = await BackendRouteTestHost.StartAsync(app);
    using var client = new HttpClient { BaseAddress = baseAddress };
    using var content = new StringContent(
      """{"status":"succeeded","providerJobId":"provider-job-1","assetUrl":"https://example.invalid/video.mp4"}""",
      Encoding.UTF8,
      "application/json"
    );

    var response = await client.PostAsync("/v1/provider-callbacks/job-1", content);

    Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    var body = await response.Content.ReadAsStringAsync();
    Assert.Contains("Provider callbacks are not configured.", body);
  }

  [Fact]
  public void CreateThrowsWhenEnabledProviderIsMissingApiKey()
  {
    var error = Assert.Throws<InvalidOperationException>(() => GifForgeBackendApp.Create(args: [
      "--GIFFORGE_FAL_ENABLED=true",
      "--GIFFORGE_LUMA_ENABLED=false"
    ]));

    Assert.Contains("GIFFORGE_FAL_API_KEY", error.Message);
  }

  private static JsonSerializerOptions JsonOptions() =>
    new(JsonSerializerDefaults.Web);

  private static string[] ProviderCostArgs() =>
  [
    .. FalCostArgs(),
    "--GIFFORGE_MODEL_COST_USD_LUMA_RAY32_TEXT_TO_VIDEO=0.16",
    "--GIFFORGE_MODEL_COST_USD_LUMA_RAY32_IMAGE_TO_VIDEO=0.18",
    "--GIFFORGE_MODEL_COST_USD_LUMA_RAY32_VIDEO_TO_VIDEO=0.22"
  ];

  private static string[] FalCostArgs() =>
  [
    "--GIFFORGE_MODEL_COST_USD_FAL_WAN22_TEXT_TO_VIDEO=0.03",
    "--GIFFORGE_MODEL_COST_USD_FAL_WAN22_IMAGE_TO_VIDEO=0.04",
    "--GIFFORGE_MODEL_COST_USD_FAL_WAN22_VIDEO_TO_VIDEO=0.05"
  ];
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

  public string Mode => "test";

  public GenerationRequest? SubmittedRequest { get; private set; }

  public Task<ProviderJob> SubmitGenerationAsync(GenerationRequest request, CancellationToken cancellationToken)
  {
    SubmittedRequest = request;
    return Task.FromResult(new ProviderJob(Name, "recording-provider-job"));
  }

  public Task<GeneratedMotionResult> GetResultAsync(GenerationJob job, CancellationToken cancellationToken) =>
    throw new NotSupportedException();
}

internal sealed class SubmitFailureProvider : IGenerationProvider
{
  private readonly Exception error;

  public SubmitFailureProvider(Exception error)
  {
    this.error = error;
  }

  public string Name => "submit-failure-provider";

  public string Mode => "test";

  public Task<ProviderJob> SubmitGenerationAsync(GenerationRequest request, CancellationToken cancellationToken) =>
    Task.FromException<ProviderJob>(error);

  public Task<GeneratedMotionResult> GetResultAsync(GenerationJob job, CancellationToken cancellationToken) =>
    throw new NotSupportedException();
}

internal sealed class RecordingRetryGenerationProvider : IRetryAwareGenerationProvider
{
  public string Name => "retry-recording-provider";

  public string Mode => "test";

  public string? RetryOfJobId { get; private set; }

  public HashSet<string> AttemptedProviders { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

  public HashSet<string> AttemptedModelIds { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

  public GenerationRequest? SubmittedRequest { get; private set; }

  public Task<ProviderJob> SubmitGenerationAsync(GenerationRequest request, CancellationToken cancellationToken)
  {
    SubmittedRequest = request;
    return Task.FromResult(new ProviderJob(Name, "provider-job-1", "first-model"));
  }

  public Task<ProviderJob> SubmitRetryGenerationAsync(
    GenerationRequest request,
    IReadOnlySet<string> attemptedProviders,
    IReadOnlySet<string> attemptedModelIds,
    CancellationToken cancellationToken
  )
  {
    SubmittedRequest = request;
    RetryOfJobId = request.RetryOfJobId;
    AttemptedProviders = attemptedProviders.ToHashSet(StringComparer.OrdinalIgnoreCase);
    AttemptedModelIds = attemptedModelIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
    return Task.FromResult(new ProviderJob(Name, "provider-job-2", "retry-model"));
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
