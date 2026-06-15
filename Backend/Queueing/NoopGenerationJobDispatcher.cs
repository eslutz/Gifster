using Gifster.Backend.Jobs;

namespace Gifster.Backend.Queueing;

public sealed class NoopGenerationJobDispatcher : IGenerationJobDispatcher
{
  public Task DispatchAsync(GenerationJob job, CancellationToken cancellationToken) =>
    Task.CompletedTask;
}
