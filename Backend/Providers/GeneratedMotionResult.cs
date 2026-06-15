using System.Text.Json;
using System.Text.Json.Serialization;
using Gifster.Backend.Models;
using Gifster.Backend.Storage;

namespace Gifster.Backend.Providers;

public sealed record GeneratedMotionResult(string ContentType, byte[] Bytes)
{
  public static GeneratedMotionResult FromFrameSequence(FrameSequenceAsset asset)
  {
    var bytes = JsonSerializer.SerializeToUtf8Bytes(
      asset,
      GeneratedMotionResultJsonSerializerContext.Default.FrameSequenceAsset
    );
    return new GeneratedMotionResult(GenerationResultContentTypes.FrameSequence, bytes);
  }

  public FrameSequenceAsset ToFrameSequence() =>
    JsonSerializer.Deserialize(
      Bytes,
      GeneratedMotionResultJsonSerializerContext.Default.FrameSequenceAsset
    ) ?? throw new InvalidOperationException("Generated motion result is not a frame sequence.");
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(FrameSequenceAsset))]
[JsonSerializable(typeof(FrameSpec))]
internal partial class GeneratedMotionResultJsonSerializerContext : JsonSerializerContext;
