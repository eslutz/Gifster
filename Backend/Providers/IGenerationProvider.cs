using Gifster.Backend.Jobs;
using Gifster.Backend.Models;

namespace Gifster.Backend.Providers;

public interface IGenerationProvider
{
  string Name { get; }
  Task<ProviderJob> SubmitGenerationAsync(GenerationRequest request, CancellationToken cancellationToken);
  Task<GeneratedMotionResult> GetResultAsync(GenerationJob job, CancellationToken cancellationToken);
}

public sealed record ProviderJob(string Provider, string ProviderJobId);
