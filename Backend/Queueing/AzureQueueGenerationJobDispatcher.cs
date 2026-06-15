using System.Text.Json;
using Azure.Storage.Queues;
using Gifster.Backend.Jobs;

namespace Gifster.Backend.Queueing;

public sealed class AzureQueueGenerationJobDispatcher : IGenerationJobDispatcher
{
  private readonly QueueClient queueClient;

  public AzureQueueGenerationJobDispatcher(QueueClient queueClient)
  {
    this.queueClient = queueClient;
  }

  public async Task DispatchAsync(GenerationJob job, CancellationToken cancellationToken)
  {
    var message = new GenerationQueueMessage(job.Id);
    var json = JsonSerializer.Serialize(message, GifsterJsonSerializerContext.Default.GenerationQueueMessage);
    await queueClient.SendMessageAsync(json, cancellationToken).ConfigureAwait(false);
  }
}
