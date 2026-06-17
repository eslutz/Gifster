using GifForge.Backend.Jobs;
using GifForge.Backend.Models;
using GifForge.Backend.Providers;
using Microsoft.Extensions.Configuration;

namespace GifForge.Backend.Tests;

public sealed class ConfiguredGenerationProviderTests
{
  [Fact]
  public async Task SubmitGenerationAsyncUsesLatestConfiguration()
  {
    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?>
      {
        ["GIFFORGE_TEST_PROVIDER_NAME"] = "first"
      })
      .Build();
    var provider = new ConfiguredGenerationProvider(
      configuration,
      current => new NamedGenerationProvider(current["GIFFORGE_TEST_PROVIDER_NAME"] ?? "missing")
    );

    var first = await provider.SubmitGenerationAsync(TestGenerationRequests.Valid(), CancellationToken.None);
    configuration["GIFFORGE_TEST_PROVIDER_NAME"] = "second";

    var second = await provider.SubmitGenerationAsync(TestGenerationRequests.Valid(), CancellationToken.None);

    Assert.Equal("first", first.Provider);
    Assert.Equal("second", second.Provider);
    Assert.Equal("second", provider.Name);
  }

  private sealed class NamedGenerationProvider(string name) : IGenerationProvider
  {
    public string Name => name;

    public string Mode => "test";

    public Task<ProviderJob> SubmitGenerationAsync(GenerationRequest request, CancellationToken cancellationToken) =>
      Task.FromResult(new ProviderJob(Name, $"{Name}-job"));

    public Task<GeneratedMotionResult> GetResultAsync(GenerationJob job, CancellationToken cancellationToken) =>
      throw new NotSupportedException();
  }
}
