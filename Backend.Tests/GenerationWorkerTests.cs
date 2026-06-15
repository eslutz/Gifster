using Gifster.Backend.Jobs;
using Gifster.Backend.Operations;
using Gifster.Backend.Providers;
using Gifster.Backend.Storage;

namespace Gifster.Backend.Tests;

public sealed class GenerationWorkerTests
{
  [Fact]
  public async Task ProcessJobAsyncStoresProviderResultAndMarksJobSucceeded()
  {
    var table = new InMemoryGenerationJobTable();
    var store = new TableGenerationJobStore(table);
    var provider = new FakeFrameSequenceProvider();
    var resultStore = new InMemoryGenerationResultStore();
    var worker = new GenerationWorker(store, provider, resultStore);
    var providerJob = await provider.SubmitGenerationAsync(TestGenerationRequests.Valid(), CancellationToken.None);
    var job = await store.CreateAsync(TestGenerationRequests.Valid(), providerJob, CancellationToken.None);

    await worker.ProcessJobAsync(job.Id, CancellationToken.None);

    var completed = await store.GetAsync(job.Id, CancellationToken.None);
    Assert.NotNull(completed);
    Assert.Equal(GenerationJobStatus.Succeeded, completed.Status);
    Assert.Equal("application/vnd.gifster.frame-sequence+json", completed.ResultContentType);
    Assert.NotNull(completed.ResultBlobName);
    var saved = await resultStore.ReadAsync(completed, CancellationToken.None);
    Assert.Equal("application/vnd.gifster.frame-sequence+json", saved.ContentType);
    Assert.Equal("frame-sequence-v1", saved.ToFrameSequence().Format);
  }

  [Fact]
  public async Task ProcessJobAsyncRecordsSanitizedLifecycleEvents()
  {
    const string secretPrompt = "SECRET_WORKER_PROMPT";
    var table = new InMemoryGenerationJobTable();
    var store = new TableGenerationJobStore(table);
    var provider = new FakeFrameSequenceProvider();
    var resultStore = new InMemoryGenerationResultStore();
    var eventSink = new RecordingGenerationEventSink();
    var worker = new GenerationWorker(store, provider, resultStore, eventSink);
    var providerJob = await provider.SubmitGenerationAsync(TestGenerationRequests.Valid(secretPrompt), CancellationToken.None);
    var job = await store.CreateAsync(TestGenerationRequests.Valid(secretPrompt), providerJob, CancellationToken.None);

    await worker.ProcessJobAsync(job.Id, CancellationToken.None);

    Assert.Collection(
      eventSink.Events,
      running =>
      {
        Assert.Equal("generation.running", running.Name);
        Assert.Equal(job.Id, running.JobId);
        Assert.Equal("text_to_gif", running.Mode);
        Assert.Equal("none", running.CaptionMode);
      },
      succeeded =>
      {
        Assert.Equal("generation.succeeded", succeeded.Name);
        Assert.Equal(job.Id, succeeded.JobId);
        Assert.Equal("application/vnd.gifster.frame-sequence+json", succeeded.ResultContentType);
      }
    );
    Assert.DoesNotContain(secretPrompt, eventSink.SerializedEvents);
  }

  [Fact]
  public async Task ProcessJobAsyncMarksMissingJobsFailedByIgnoringThem()
  {
    var worker = new GenerationWorker(
      new TableGenerationJobStore(new InMemoryGenerationJobTable()),
      new FakeFrameSequenceProvider(),
      new InMemoryGenerationResultStore()
    );

    await worker.ProcessJobAsync("missing", CancellationToken.None);
  }

  [Fact]
  public async Task ProcessJobAsyncIgnoresExpiredJobsWithoutCallingProvider()
  {
    var table = new InMemoryGenerationJobTable();
    var store = new TableGenerationJobStore(table);
    var provider = new ThrowingResultProvider(new InvalidOperationException("provider should not be called"));
    var resultStore = new InMemoryGenerationResultStore();
    var worker = new GenerationWorker(store, provider, resultStore);
    var job = await store.CreateAsync(
      TestGenerationRequests.Valid(),
      new ProviderJob(provider.Name, "provider-job-1"),
      CancellationToken.None
    );
    await store.SaveAsync(job with { ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) }, CancellationToken.None);

    await worker.ProcessJobAsync(job.Id, CancellationToken.None);

    var unchanged = await store.GetAsync(job.Id, CancellationToken.None);
    Assert.NotNull(unchanged);
    Assert.Equal(GenerationJobStatus.Queued, unchanged.Status);
  }

  [Fact]
  public async Task ProcessJobAsyncPropagatesRetryableProviderFailuresSoQueueCanRetry()
  {
    var table = new InMemoryGenerationJobTable();
    var store = new TableGenerationJobStore(table);
    var provider = new ThrowingResultProvider(new HttpRequestException("provider is not ready"));
    var resultStore = new InMemoryGenerationResultStore();
    var worker = new GenerationWorker(store, provider, resultStore);
    var job = await store.CreateAsync(
      TestGenerationRequests.Valid(),
      new ProviderJob(provider.Name, "provider-job-1"),
      CancellationToken.None
    );

    await Assert.ThrowsAsync<HttpRequestException>(
      () => worker.ProcessJobAsync(job.Id, CancellationToken.None)
    );

    var running = await store.GetAsync(job.Id, CancellationToken.None);
    Assert.NotNull(running);
    Assert.Equal(GenerationJobStatus.Running, running.Status);
    Assert.Null(running.FailedMessage);
  }

  [Fact]
  public async Task ProcessJobAsyncMarksPermanentProviderFailuresFailed()
  {
    var table = new InMemoryGenerationJobTable();
    var store = new TableGenerationJobStore(table);
    var provider = new ThrowingResultProvider(new GenerationPermanentFailureException("provider rejected request"));
    var resultStore = new InMemoryGenerationResultStore();
    var worker = new GenerationWorker(store, provider, resultStore);
    var job = await store.CreateAsync(
      TestGenerationRequests.Valid(),
      new ProviderJob(provider.Name, "provider-job-1"),
      CancellationToken.None
    );

    await worker.ProcessJobAsync(job.Id, CancellationToken.None);

    var failed = await store.GetAsync(job.Id, CancellationToken.None);
    Assert.NotNull(failed);
    Assert.Equal(GenerationJobStatus.Failed, failed.Status);
    Assert.Equal("provider rejected request", failed.FailedMessage);
  }

  [Fact]
  public async Task ProcessJobAsyncRecordsSanitizedPermanentFailureEvent()
  {
    const string secretPrompt = "SECRET_FAILURE_PROMPT";
    var table = new InMemoryGenerationJobTable();
    var store = new TableGenerationJobStore(table);
    var provider = new ThrowingResultProvider(new GenerationPermanentFailureException(
      $"provider rejected {secretPrompt}"
    ));
    var resultStore = new InMemoryGenerationResultStore();
    var eventSink = new RecordingGenerationEventSink();
    var worker = new GenerationWorker(store, provider, resultStore, eventSink);
    var job = await store.CreateAsync(
      TestGenerationRequests.Valid(secretPrompt),
      new ProviderJob(provider.Name, "provider-job-1"),
      CancellationToken.None
    );

    await worker.ProcessJobAsync(job.Id, CancellationToken.None);

    Assert.Collection(
      eventSink.Events,
      running => Assert.Equal("generation.running", running.Name),
      failed =>
      {
        Assert.Equal("generation.failed", failed.Name);
        Assert.Equal("permanent_provider_failure", failed.FailureKind);
      }
    );
    Assert.DoesNotContain(secretPrompt, eventSink.SerializedEvents);
  }
}

internal sealed class ThrowingResultProvider : IGenerationProvider
{
  private readonly Exception error;

  public ThrowingResultProvider(Exception error)
  {
    this.error = error;
  }

  public string Name => "throwing-provider";

  public string Mode => "test";

  public Task<ProviderJob> SubmitGenerationAsync(
    Gifster.Backend.Models.GenerationRequest request,
    CancellationToken cancellationToken
  ) =>
    Task.FromResult(new ProviderJob(Name, "provider-job-1"));

  public Task<GeneratedMotionResult> GetResultAsync(GenerationJob job, CancellationToken cancellationToken) =>
    Task.FromException<GeneratedMotionResult>(error);
}
