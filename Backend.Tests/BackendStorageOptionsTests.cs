using Gifster.Backend.Configuration;
using Microsoft.Extensions.Configuration;

namespace Gifster.Backend.Tests;

public sealed class BackendStorageOptionsTests
{
  [Fact]
  public void FromConfigurationUsesAzureStorageResourceNames()
  {
    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?>
      {
        ["GIFSTER_STORAGE_ACCOUNT_NAME"] = "gifsternonprod",
        ["GIFSTER_JOBS_TABLE_NAME"] = "GenerationJobs",
        ["GIFSTER_GENERATION_QUEUE_NAME"] = "generation-jobs",
        ["GIFSTER_RESULTS_CONTAINER_NAME"] = "provider-results",
        ["AZURE_CLIENT_ID"] = "11111111-1111-1111-1111-111111111111"
      })
      .Build();

    var options = BackendStorageOptions.FromConfiguration(configuration);

    Assert.True(options.IsConfigured);
    Assert.Equal("gifsternonprod", options.StorageAccountName);
    Assert.Equal("GenerationJobs", options.JobsTableName);
    Assert.Equal("generation-jobs", options.GenerationQueueName);
    Assert.Equal("provider-results", options.ResultsContainerName);
    Assert.Equal("11111111-1111-1111-1111-111111111111", options.ManagedIdentityClientId);
  }

  [Fact]
  public void FromConfigurationKeepsLocalModeWhenStorageAccountIsMissing()
  {
    var options = BackendStorageOptions.FromConfiguration(new ConfigurationBuilder().Build());

    Assert.False(options.IsConfigured);
    Assert.Equal("GenerationJobs", options.JobsTableName);
  }
}
