using Gifster.Backend.Models;
using Gifster.Backend.Providers;
using Gifster.Backend.Jobs;
using Gifster.Backend.Storage;

namespace Gifster.Backend.Tests;

public sealed class FakeProviderTests
{
  [Fact]
  public async Task FakeProviderReturnsDeterministicFrameSequence()
  {
    var provider = new FakeFrameSequenceProvider();
    var request = TestGenerationRequests.Valid();

    var providerJob = await provider.SubmitGenerationAsync(request, CancellationToken.None);
    var job = GenerationJob.Create(request, providerJob);
    var result = await provider.GetResultAsync(job, CancellationToken.None);
    var frameSequence = result.ToFrameSequence();

    Assert.Equal("fake-frame-sequence", provider.Name);
    Assert.Equal("fake-frame-sequence", providerJob.Provider);
    Assert.StartsWith("fake_", providerJob.ProviderJobId);
    Assert.Equal(GenerationResultContentTypes.FrameSequence, result.ContentType);
    Assert.NotEmpty(result.Bytes);
    Assert.Equal("frame-sequence-v1", frameSequence.Format);
    Assert.Equal(18, frameSequence.Frames.Count);
    Assert.Equal(480, frameSequence.Width);
    Assert.Equal(360, frameSequence.Height);
  }
}
