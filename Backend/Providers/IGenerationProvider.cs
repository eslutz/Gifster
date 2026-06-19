using GifForge.Backend.Jobs;
using GifForge.Backend.Models;

namespace GifForge.Backend.Providers;

public interface IGenerationProvider
{
  string Name { get; }
  string Mode { get; }
  Task<ProviderJob> SubmitGenerationAsync(GenerationRequest request, CancellationToken cancellationToken);
  Task<GeneratedMotionResult> GetResultAsync(GenerationJob job, CancellationToken cancellationToken);
}

public interface IGenerationCreditEstimator
{
  GenerationCreditEstimate EstimateGenerationCredits(
    GenerationRequest request,
    IReadOnlySet<string> attemptedProviders,
    IReadOnlySet<string> attemptedModelIds
  );
}

public sealed record ProviderJob(string Provider, string ProviderJobId, string? ModelId = null);

public sealed record GenerationCreditEstimate(
  int RequiredCredits,
  string Provider,
  string? ModelId,
  VideoGenerationCapability? Capability
);
