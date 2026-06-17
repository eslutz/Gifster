using GifForge.Backend.Jobs;
using GifForge.Backend.Models;

namespace GifForge.Backend.Providers;

public interface IVideoGenerationProvider
{
  string Name { get; }
  IReadOnlyList<VideoGenerationModel> Models { get; }

  Task<ProviderJob> GenerateFromTextAsync(
    GenerationRequest request,
    VideoGenerationModel model,
    CancellationToken cancellationToken
  );

  Task<ProviderJob> GenerateFromImageAsync(
    GenerationRequest request,
    VideoGenerationModel model,
    CancellationToken cancellationToken
  );

  Task<ProviderJob> TransformVideoAsync(
    GenerationRequest request,
    VideoGenerationModel model,
    CancellationToken cancellationToken
  );

  Task<GeneratedMotionResult> GetResultAsync(GenerationJob job, CancellationToken cancellationToken);
}

public sealed record VideoGenerationModel(
  string Key,
  string ModelId,
  VideoGenerationCapability Capability,
  decimal EstimatedCostUsd,
  bool Enabled
)
{
  public VideoGenerationModel(
    string modelId,
    VideoGenerationCapability capability,
    decimal estimatedCostUsd,
    bool enabled
  )
    : this(modelId, modelId, capability, estimatedCostUsd, enabled)
  {
  }

  public string CostConfigurationKey => $"GIFFORGE_MODEL_COST_USD_{Key}";
}

public enum VideoGenerationCapability
{
  TextToVideo,
  ImageToVideo,
  VideoToVideo
}
