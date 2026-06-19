using GifForge.Backend.Jobs;
using GifForge.Backend.Models;
using Microsoft.Extensions.Logging;

namespace GifForge.Backend.Providers;

public interface IRetryAwareGenerationProvider : IGenerationProvider
{
  Task<ProviderJob> SubmitRetryGenerationAsync(
    GenerationRequest request,
    IReadOnlySet<string> attemptedProviders,
    IReadOnlySet<string> attemptedModelIds,
    CancellationToken cancellationToken
  );
}

public sealed class RoutedVideoGenerationProvider : IRetryAwareGenerationProvider, IGenerationCreditEstimator
{
  private readonly IReadOnlyList<IVideoGenerationProvider> providers;
  private readonly ILogger<RoutedVideoGenerationProvider>? logger;

  public RoutedVideoGenerationProvider(
    IReadOnlyList<IVideoGenerationProvider> providers,
    ILogger<RoutedVideoGenerationProvider>? logger = null
  )
  {
    if (providers.Count == 0)
    {
      throw new InvalidOperationException("At least one video generation provider must be configured.");
    }

    this.providers = providers;
    this.logger = logger;
  }

  public string Name => "routed-video";

  public string Mode => "video";

  public GenerationCreditEstimate EstimateGenerationCredits(
    GenerationRequest request,
    IReadOnlySet<string> attemptedProviders,
    IReadOnlySet<string> attemptedModelIds
  )
  {
    var capability = VideoGenerationInputClassifier.RequiredCapability(request);
    var candidate = CandidateModels(capability, attemptedProviders, attemptedModelIds).FirstOrDefault();
    if (candidate is null)
    {
      throw new GenerationPermanentFailureException(
        $"No enabled provider model supports {capability}."
      );
    }

    return new GenerationCreditEstimate(
      RequiredCreditsFor(candidate.Provider.Name, capability),
      candidate.Provider.Name,
      candidate.Model.ModelId,
      capability
    );
  }

  public async Task<ProviderJob> SubmitGenerationAsync(
    GenerationRequest request,
    CancellationToken cancellationToken
  ) =>
    await SubmitGenerationAsync(
      request,
      new HashSet<string>(StringComparer.OrdinalIgnoreCase),
      new HashSet<string>(StringComparer.OrdinalIgnoreCase),
      cancellationToken
    ).ConfigureAwait(false);

  public async Task<ProviderJob> SubmitRetryGenerationAsync(
    GenerationRequest request,
    IReadOnlySet<string> attemptedProviders,
    IReadOnlySet<string> attemptedModelIds,
    CancellationToken cancellationToken
  ) =>
    await SubmitGenerationAsync(request, attemptedProviders, attemptedModelIds, cancellationToken).ConfigureAwait(false);

  private async Task<ProviderJob> SubmitGenerationAsync(
    GenerationRequest request,
    IReadOnlySet<string> attemptedProviders,
    IReadOnlySet<string> attemptedModelIds,
    CancellationToken cancellationToken
  )
  {
    var capability = VideoGenerationInputClassifier.RequiredCapability(request);
    var candidates = CandidateModels(capability, attemptedProviders, attemptedModelIds).ToArray();
    if (candidates.Length == 0)
    {
      throw new GenerationPermanentFailureException(
        $"No enabled provider model supports {capability}."
      );
    }

    var candidate = candidates[0];
    try
    {
      logger?.LogInformation(
        "Submitting generation to {ProviderName} model {ModelId} for capability {Capability}",
        candidate.Provider.Name,
        candidate.Model.ModelId,
        capability
      );

      var providerJob = capability switch
      {
        VideoGenerationCapability.TextToVideo => await candidate.Provider
          .GenerateFromTextAsync(request, candidate.Model, cancellationToken)
          .ConfigureAwait(false),
        VideoGenerationCapability.ImageToVideo => await candidate.Provider
          .GenerateFromImageAsync(request, candidate.Model, cancellationToken)
          .ConfigureAwait(false),
        VideoGenerationCapability.VideoToVideo => await candidate.Provider
          .TransformVideoAsync(request, candidate.Model, cancellationToken)
          .ConfigureAwait(false),
        _ => throw new GenerationPermanentFailureException($"Unsupported capability {capability}.")
      };
      return providerJob.ModelId is null
        ? providerJob with { ModelId = candidate.Model.ModelId }
        : providerJob;
    }
    catch (TaskCanceledException error) when (!cancellationToken.IsCancellationRequested)
    {
      throw new HttpRequestException(
        $"Provider {candidate.Provider.Name} timed out during submission.",
        error
      );
    }
  }

  public async Task<GeneratedMotionResult> GetResultAsync(
    GenerationJob job,
    CancellationToken cancellationToken
  )
  {
    var provider = providers.FirstOrDefault(item => string.Equals(item.Name, job.Provider, StringComparison.OrdinalIgnoreCase));
    if (provider is null)
    {
      throw new GenerationPermanentFailureException($"Configured provider '{job.Provider}' is not available.");
    }

    return await provider.GetResultAsync(job, cancellationToken).ConfigureAwait(false);
  }

  private IEnumerable<ProviderModelCandidate> CandidateModels(
    VideoGenerationCapability capability,
    IReadOnlySet<string> attemptedProviders,
    IReadOnlySet<string> attemptedModelIds
  ) =>
    providers
      .SelectMany(provider => provider.Models
        .Where(model =>
          model.Enabled &&
          model.Capability == capability &&
          !attemptedProviders.Contains(provider.Name) &&
          !attemptedModelIds.Contains(model.ModelId))
        .Select(model => new ProviderModelCandidate(provider, model)))
      .OrderBy(candidate => candidate.Model.EstimatedCostUsd)
      .ThenBy(candidate => candidate.Provider.Name is "fal.ai" ? 0 : 1)
      .ThenBy(candidate => candidate.Provider.Name, StringComparer.OrdinalIgnoreCase)
      .ThenBy(candidate => candidate.Model.ModelId, StringComparer.OrdinalIgnoreCase);

  private static int RequiredCreditsFor(string providerName, VideoGenerationCapability capability)
  {
    if (string.Equals(providerName, "luma", StringComparison.OrdinalIgnoreCase))
    {
      return capability == VideoGenerationCapability.VideoToVideo ? 5 : 2;
    }

    return capability == VideoGenerationCapability.VideoToVideo ? 2 : 1;
  }

  private sealed record ProviderModelCandidate(IVideoGenerationProvider Provider, VideoGenerationModel Model);
}
