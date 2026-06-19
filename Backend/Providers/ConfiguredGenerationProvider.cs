using GifForge.Backend.Configuration;
using GifForge.Backend.Jobs;
using GifForge.Backend.Models;
using Microsoft.Extensions.Configuration;

namespace GifForge.Backend.Providers;

public sealed class ConfiguredGenerationProvider : IRetryAwareGenerationProvider, IGenerationCreditEstimator
{
  private readonly IConfiguration configuration;
  private readonly Func<IConfiguration, IGenerationProvider> providerFactory;

  public ConfiguredGenerationProvider(IConfiguration configuration)
    : this(configuration, CreateRoutedVideoProvider)
  {
    _ = CurrentProvider();
  }

  internal ConfiguredGenerationProvider(
    IConfiguration configuration,
    Func<IConfiguration, IGenerationProvider> providerFactory
  )
  {
    this.configuration = configuration;
    this.providerFactory = providerFactory;
  }

  public string Name => CurrentProvider().Name;

  public string Mode => CurrentProvider().Mode;

  public GenerationCreditEstimate EstimateGenerationCredits(
    GenerationRequest request,
    IReadOnlySet<string> attemptedProviders,
    IReadOnlySet<string> attemptedModelIds
  )
  {
    var provider = CurrentProvider();
    return provider is IGenerationCreditEstimator estimator
      ? estimator.EstimateGenerationCredits(request, attemptedProviders, attemptedModelIds)
      : new GenerationCreditEstimate(1, provider.Name, null, null);
  }

  public async Task<ProviderJob> SubmitGenerationAsync(
    GenerationRequest request,
    CancellationToken cancellationToken
  ) =>
    await CurrentProvider()
      .SubmitGenerationAsync(request, cancellationToken)
      .ConfigureAwait(false);

  public async Task<ProviderJob> SubmitRetryGenerationAsync(
    GenerationRequest request,
    IReadOnlySet<string> attemptedProviders,
    IReadOnlySet<string> attemptedModelIds,
    CancellationToken cancellationToken
  )
  {
    var provider = CurrentProvider();
    if (provider is not IRetryAwareGenerationProvider retryAwareProvider)
    {
      throw new InvalidOperationException(
        $"Configured generation provider {provider.Name} does not support retry-aware submissions."
      );
    }

    return await retryAwareProvider
      .SubmitRetryGenerationAsync(request, attemptedProviders, attemptedModelIds, cancellationToken)
      .ConfigureAwait(false);
  }

  public async Task<GeneratedMotionResult> GetResultAsync(
    GenerationJob job,
    CancellationToken cancellationToken
  ) =>
    await CurrentProvider()
      .GetResultAsync(job, cancellationToken)
      .ConfigureAwait(false);

  private IGenerationProvider CurrentProvider() =>
    providerFactory(configuration);

  private static IGenerationProvider CreateRoutedVideoProvider(IConfiguration configuration)
  {
    var providers = new List<IVideoGenerationProvider>();
    if (VideoProviderConfiguration.IsEnabled(configuration, "FAL", "GIFFORGE_FAL_API_KEY"))
    {
      providers.Add(new FalVideoProvider(VideoProviderConfiguration.Fal(configuration), new HttpClient()));
    }

    if (VideoProviderConfiguration.IsEnabled(configuration, "LUMA", "GIFFORGE_LUMA_API_KEY"))
    {
      providers.Add(new LumaVideoProvider(VideoProviderConfiguration.Luma(configuration), new HttpClient()));
    }

    return new RoutedVideoGenerationProvider(providers);
  }
}
