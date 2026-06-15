using Gifster.Backend.Models;
using Gifster.Backend.Providers;

namespace Gifster.Backend.Jobs;

public interface IJobStore
{
  Task<GenerationJob> CreateAsync(GenerationRequest request, ProviderJob providerJob, CancellationToken cancellationToken);
  Task<GenerationJob?> GetAsync(string id, CancellationToken cancellationToken);
  Task SaveAsync(GenerationJob job, CancellationToken cancellationToken);
  GenerationJobStatus StatusFor(GenerationJob job);
}
