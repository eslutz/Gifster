using Gifster.Backend.Queueing;

namespace Gifster.Backend.Tests;

public sealed class AzureQueueGenerationJobReaderTests
{
  [Fact]
  public void EmptyQueueMessageReturnsNoDequeuedJob()
  {
    Assert.Null(AzureQueueGenerationJobReader.ToDequeuedJob(null));
  }
}
