using System.Security.Cryptography;
using System.Text;
using Gifster.Backend.Jobs;
using Gifster.Backend.Models;

namespace Gifster.Backend.Providers;

public sealed class FakeFrameSequenceProvider : IGenerationProvider
{
  private static readonly string[][] Palettes =
  [
    ["#006D77", "#83C5BE", "#EDF6F9", "#FFDDD2", "#E29578"],
    ["#264653", "#2A9D8F", "#E9C46A", "#F4A261", "#E76F51"],
    ["#0B132B", "#5BC0BE", "#F7FFF7", "#FFE66D", "#FF6B6B"],
    ["#233D4D", "#A1C181", "#FE7F2D", "#FCCA46", "#619B8A"]
  ];

  public string Name => "fake-frame-sequence";

  public string Mode => "demo";

  public Task<ProviderJob> SubmitGenerationAsync(GenerationRequest request, CancellationToken cancellationToken)
  {
    var seed = request.ClientTraceId ?? request.CleanedPrompt;
    var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed))).ToLowerInvariant();
    return Task.FromResult(new ProviderJob(Name, $"fake_{hash[..16]}"));
  }

  public Task<GeneratedMotionResult> GetResultAsync(GenerationJob job, CancellationToken cancellationToken)
  {
    var request = job.Request;
    var palette = PaletteForPrompt(request.CleanedPrompt);
    const int frameCount = 18;
    var width = request.Options?.Width ?? 480;
    var height = request.Options?.Height ?? 360;
    var loopSeconds = request.Options?.LoopSeconds ?? 2.4;
    var duration = Math.Round(loopSeconds / frameCount, 3, MidpointRounding.AwayFromZero);

    var frames = Enumerable.Range(0, frameCount)
      .Select(index => new FrameSpec(
        index,
        duration,
        palette[index % palette.Length],
        palette[(index + 2) % palette.Length],
        Math.Sin((index / (double)frameCount) * Math.PI * 2) * 48
      ))
      .ToArray();

    var asset = new FrameSequenceAsset(
      "frame-sequence-v1",
      width,
      height,
      frames,
      request.CleanedPrompt
    );
    return Task.FromResult(GeneratedMotionResult.FromFrameSequence(asset));
  }

  private static string[] PaletteForPrompt(string prompt)
  {
    var digest = SHA256.HashData(Encoding.UTF8.GetBytes(prompt));
    return Palettes[digest[0] % Palettes.Length];
  }
}
