namespace Gifster.Backend.Models;

public sealed record GenerationRequest(
  string? Id,
  string Mode,
  string? OriginalPrompt,
  string CleanedPrompt,
  string? ExpandedPrompt,
  string? NegativePrompt,
  CaptionRequest? Caption,
  SourceImageRequest? SourceImage,
  SourceImageContextRequest? SourceImageContext,
  GenerationOptions? Options,
  string? ClientTraceId
);

public sealed record CaptionRequest(string Mode, string? Text);

public sealed record SourceImageRequest(
  string DataBase64,
  string MimeType,
  int Width,
  int Height
);

public sealed record SourceImageContextRequest(
  int Width,
  int Height,
  string? Orientation,
  string? AspectRatio,
  string? Summary
);

public sealed record GenerationOptions(
  int? Width,
  int? Height,
  double? LoopSeconds,
  string? StylePreset,
  string? MotionIntensity
);
