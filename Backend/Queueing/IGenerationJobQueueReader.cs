namespace Gifster.Backend.Queueing;

public interface IGenerationJobQueueReader
{
  Task<DequeuedGenerationJob?> DequeueAsync(CancellationToken cancellationToken);
  Task CompleteAsync(DequeuedGenerationJob job, CancellationToken cancellationToken);
}

public sealed record DequeuedGenerationJob(string JobId, string MessageId, string PopReceipt);
