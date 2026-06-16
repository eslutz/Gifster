using GifForge.Backend.Configuration;
using GifForge.Backend.Providers;
using Microsoft.Extensions.Configuration;

namespace GifForge.Backend.Tests;

public sealed class VideoProviderConfigurationTests
{
  [Fact]
  public void FalModelsUseCodeDefinedModelIdsAndCostOverrides()
  {
    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?>
      {
        ["GIFFORGE_FAL_API_KEY"] = "fal-test-key",
        [$"GIFFORGE_FAL_{"TEXT"}_MODEL"] = "config-should-not-select-model",
        ["GIFFORGE_MODEL_COST_USD_FAL_WAN22_TEXT_TO_VIDEO"] = "0.0123"
      })
      .Build();

    var options = VideoProviderConfiguration.Fal(configuration);

    var textModel = Assert.Single(options.Models, model => model.Capability == VideoGenerationCapability.TextToVideo);
    Assert.Equal("fal-ai/wan/v2.2-a14b/text-to-video", textModel.ModelId);
    Assert.Equal(0.0123m, textModel.EstimatedCostUsd);
    Assert.Equal("https://queue.fal.run/{modelId}/requests/{providerJobId}", options.ResultUrlTemplate);
  }

  [Fact]
  public void LumaModelsUseCodeDefinedModelIdsAndCostOverrides()
  {
    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?>
      {
        ["GIFFORGE_LUMA_API_KEY"] = "luma-test-key",
        [$"GIFFORGE_LUMA_{"VIDEO"}_MODEL"] = "config-should-not-select-model",
        ["GIFFORGE_MODEL_COST_USD_LUMA_RAY32_VIDEO_TO_VIDEO"] = "0.091"
      })
      .Build();

    var options = VideoProviderConfiguration.Luma(configuration);

    var videoModel = Assert.Single(options.Models, model => model.Capability == VideoGenerationCapability.VideoToVideo);
    Assert.Equal("ray-3.2", videoModel.ModelId);
    Assert.Equal(0.091m, videoModel.EstimatedCostUsd);
  }
}
