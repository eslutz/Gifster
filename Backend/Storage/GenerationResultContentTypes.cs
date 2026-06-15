namespace Gifster.Backend.Storage;

public static class GenerationResultContentTypes
{
  public const string FrameSequence = "application/vnd.gifster.frame-sequence+json";
  public const string Mp4 = "video/mp4";

  public static string FileExtensionFor(string contentType) =>
    contentType.Trim().ToLowerInvariant() switch
    {
      FrameSequence => ".json",
      Mp4 => ".mp4",
      _ => ".bin"
    };
}
