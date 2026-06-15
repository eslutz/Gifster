using Gifster.Backend.Jobs;
using Gifster.Backend.Providers;

namespace Gifster.Backend.Tests;

public sealed class GenerationJobEntityTests
{
  [Fact]
  public void GenerationJobTableEntityCanBeCreatedByActivator()
  {
    var entity = Activator.CreateInstance<GenerationJobTableEntity>();

    Assert.NotNull(entity);
    Assert.Equal(GenerationJobTableEntity.JobPartitionKey, entity.PartitionKey);
  }

  [Fact]
  public void GenerationJobTableEntityRoundTripsDurableState()
  {
    var request = TestGenerationRequests.Valid();
    var providerJob = new ProviderJob("fake-frame-sequence", "fake_123");
    var job = GenerationJob.Create(request, providerJob);
    var succeeded = job with
    {
      Status = GenerationJobStatus.Succeeded,
      ResultBlobName = "results/job-1/result.json",
      ResultContentType = "application/vnd.gifster.frame-sequence+json"
    };

    var entity = GenerationJobTableEntity.FromJob(succeeded);
    var roundTrip = entity.ToJob();

    Assert.Equal("generation", entity.PartitionKey);
    Assert.Equal(succeeded.Id, entity.RowKey);
    Assert.Equal(succeeded.Id, roundTrip.Id);
    Assert.Equal(GenerationJobStatus.Succeeded, roundTrip.Status);
    Assert.Equal("fake-frame-sequence", roundTrip.Provider);
    Assert.Equal("fake_123", roundTrip.ProviderJobId);
    Assert.Equal("results/job-1/result.json", roundTrip.ResultBlobName);
    Assert.Equal("application/vnd.gifster.frame-sequence+json", roundTrip.ResultContentType);
    Assert.Equal(request.CleanedPrompt, roundTrip.Request.CleanedPrompt);
  }

  [Fact]
  public void GenerationJobTableEntityRoundTripsThroughAzureTableEntity()
  {
    var request = TestGenerationRequests.Valid();
    var providerJob = new ProviderJob("fake-frame-sequence", "fake_123");
    var job = GenerationJob.Create(request, providerJob) with
    {
      Status = GenerationJobStatus.Succeeded,
      ResultBlobName = "results/job-1/result.json",
      ResultContentType = "application/vnd.gifster.frame-sequence+json"
    };

    var tableEntity = GenerationJobTableEntity.FromJob(job).ToTableEntity();
    var entity = GenerationJobTableEntity.FromTableEntity(tableEntity);
    var roundTrip = entity.ToJob();

    Assert.Equal(job.Id, roundTrip.Id);
    Assert.Equal(GenerationJobStatus.Succeeded, roundTrip.Status);
    Assert.Equal("fake_123", roundTrip.ProviderJobId);
    Assert.Equal("results/job-1/result.json", roundTrip.ResultBlobName);
    Assert.Equal(request.ExpandedPrompt, roundTrip.Request.ExpandedPrompt);
  }
}
