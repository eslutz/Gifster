using System.Text.Json;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;

namespace Gifster.Backend.Queueing;

public sealed class AzureQueueGenerationJobReader : IGenerationJobQueueReader
{
  private readonly QueueClient queueClient;

  public AzureQueueGenerationJobReader(QueueClient queueClient)
  {
    this.queueClient = queueClient;
  }

  public async Task<DequeuedGenerationJob?> DequeueAsync(CancellationToken cancellationToken)
  {
    var response = await queueClient.ReceiveMessageAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
    if (!response.HasValue)
    {
      return null;
    }

    return ToDequeuedJob(response.Value);
  }

  internal static DequeuedGenerationJob? ToDequeuedJob(QueueMessage? message)
  {
    if (message is null)
    {
      return null;
    }

    var payload = JsonSerializer.Deserialize(
      message.MessageText,
      GifsterJsonSerializerContext.Default.GenerationQueueMessage
    );

    return payload is null
      ? null
      : new DequeuedGenerationJob(payload.JobId, message.MessageId, message.PopReceipt);
  }

  public async Task CompleteAsync(DequeuedGenerationJob job, CancellationToken cancellationToken)
  {
    await queueClient.DeleteMessageAsync(job.MessageId, job.PopReceipt, cancellationToken).ConfigureAwait(false);
  }
}
