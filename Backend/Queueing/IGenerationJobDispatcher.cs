using Gifster.Backend.Jobs;

namespace Gifster.Backend.Queueing;

public interface IGenerationJobDispatcher
{
  Task DispatchAsync(GenerationJob job, CancellationToken cancellationToken);
}
