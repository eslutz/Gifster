using Gifster.Backend.Jobs;
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
  public async Task ProcessJobAsyncMarksMissingJobsFailedByIgnoringThem()
  {
    var worker = new GenerationWorker(
      new TableGenerationJobStore(new InMemoryGenerationJobTable()),
      new FakeFrameSequenceProvider(),
      new InMemoryGenerationResultStore()
    );

    await worker.ProcessJobAsync("missing", CancellationToken.None);
  }
}
