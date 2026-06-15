using Gifster.Backend.Jobs;
using Gifster.Backend.Providers;

namespace Gifster.Backend.Tests;

public sealed class TableGenerationJobStoreTests
{
  [Fact]
  public async Task CreateAsyncPersistsJobInTableStorageShape()
  {
    var table = new InMemoryGenerationJobTable();
    var store = new TableGenerationJobStore(table);
    var request = TestGenerationRequests.Valid();

    var job = await store.CreateAsync(
      request,
      new ProviderJob("fake-frame-sequence", "fake_456"),
      CancellationToken.None
    );

    var stored = await store.GetAsync(job.Id, CancellationToken.None);

    Assert.NotNull(stored);
    Assert.Equal(job.Id, stored.Id);
    Assert.Equal(GenerationJobStatus.Queued, stored.Status);
    Assert.Equal("fake-frame-sequence", stored.Provider);
    Assert.Equal("fake_456", stored.ProviderJobId);
    Assert.Equal(request.CleanedPrompt, stored.Request.CleanedPrompt);
  }

  [Fact]
  public async Task SaveAsyncUpdatesStatusAndResultMetadata()
  {
    var table = new InMemoryGenerationJobTable();
    var store = new TableGenerationJobStore(table);
    var job = await store.CreateAsync(
      TestGenerationRequests.Valid(),
      new ProviderJob("fake-frame-sequence", "fake_789"),
      CancellationToken.None
    );

    await store.SaveAsync(job with
    {
      Status = GenerationJobStatus.Succeeded,
      ResultBlobName = "provider-results/job/result.json",
      ResultContentType = "application/vnd.gifster.frame-sequence+json",
      UpdatedAt = job.UpdatedAt.AddSeconds(2)
    }, CancellationToken.None);

    var stored = await store.GetAsync(job.Id, CancellationToken.None);

    Assert.NotNull(stored);
    Assert.Equal(GenerationJobStatus.Succeeded, stored.Status);
    Assert.Equal("provider-results/job/result.json", stored.ResultBlobName);
    Assert.Equal("application/vnd.gifster.frame-sequence+json", stored.ResultContentType);
  }
}
