using Gifster.Backend.Models;

namespace Gifster.Backend.Safety;

public static class ModerationPolicy
{
  private const int MaxProcessedImageBytes = 6_000_000;
  private const int MaxProcessedImageDimension = 1024;
  private const int MinOutputDimension = 64;
  private const int MaxOutputDimension = 1024;
  private const double MinLoopSeconds = 0.5;
  private const double MaxLoopSeconds = 6.0;

  private static readonly string[] BlockedTerms =
  [
    "child sexual",
    "minor sexual",
    "terrorist recruitment",
    "how to build a bomb"
  ];

  public static ValidationResult Validate(GenerationRequest? request)
  {
    if (request is null)
    {
      return ValidationResult.Invalid(StatusCodes.Status400BadRequest, "Request body must be a JSON object.");
    }

    if (request.Mode is not ("text_to_gif" or "image_to_gif"))
    {
      return ValidationResult.Invalid(StatusCodes.Status400BadRequest, "mode must be text_to_gif or image_to_gif.");
    }

    if (string.IsNullOrWhiteSpace(request.CleanedPrompt))
    {
      return ValidationResult.Invalid(StatusCodes.Status400BadRequest, "cleanedPrompt is required.");
    }

    if (request.CleanedPrompt.Length > 600 || (request.ExpandedPrompt ?? string.Empty).Length > 1600)
    {
      return ValidationResult.Invalid(StatusCodes.Status400BadRequest, "Prompt is too long.");
    }

    if (!string.IsNullOrEmpty(request.Caption?.Text) && request.Caption.Text.Length > 64)
    {
      return ValidationResult.Invalid(StatusCodes.Status400BadRequest, "Caption is too long.");
    }

    if (request.Caption is not null && request.Caption.Mode is not ("none" or "userText" or "suggestWithAI"))
    {
      return ValidationResult.Invalid(
        StatusCodes.Status400BadRequest,
        "caption mode must be none, userText, or suggestWithAI."
      );
    }

    var optionsValidation = ValidateOptions(request.Options);
    if (!optionsValidation.IsValid)
    {
      return optionsValidation;
    }

    if (request.Mode == "image_to_gif")
    {
      if (request.SourceImage is null || string.IsNullOrWhiteSpace(request.SourceImage.DataBase64))
      {
        return ValidationResult.Invalid(StatusCodes.Status400BadRequest, "sourceImage is required for image_to_gif.");
      }

      if (request.SourceImage.DataBase64.Length > 8_000_000)
      {
        return ValidationResult.Invalid(
          StatusCodes.Status413PayloadTooLarge,
          "sourceImage exceeds the processed upload limit."
        );
      }

      var imageValidation = ValidateProcessedSourceImage(request.SourceImage);
      if (!imageValidation.IsValid)
      {
        return imageValidation;
      }

      var sourceImageContextValidation = ValidateSourceImageContext(request.SourceImage, request.SourceImageContext);
      if (!sourceImageContextValidation.IsValid)
      {
        return sourceImageContextValidation;
      }
    }

    var searchable = string.Join(
      ' ',
      request.CleanedPrompt,
      request.ExpandedPrompt,
      request.Caption?.Text,
      request.SourceImageContext?.Summary
    ).ToLowerInvariant();
    if (BlockedTerms.Any(searchable.Contains))
    {
      return ValidationResult.Invalid(StatusCodes.Status422UnprocessableEntity, "Request failed moderation checks.");
    }

    return ValidationResult.Valid;
  }

  private static ValidationResult ValidateOptions(GenerationOptions? options)
  {
    if (options is null)
    {
      return ValidationResult.Valid;
    }

    if (options.Width is { } width && (width < MinOutputDimension || width > MaxOutputDimension))
    {
      return ValidationResult.Invalid(
        StatusCodes.Status400BadRequest,
        "options.width must be between 64 and 1024 pixels."
      );
    }

    if (options.Height is { } height && (height < MinOutputDimension || height > MaxOutputDimension))
    {
      return ValidationResult.Invalid(
        StatusCodes.Status400BadRequest,
        "options.height must be between 64 and 1024 pixels."
      );
    }

    if (options.LoopSeconds is { } loopSeconds &&
        (loopSeconds < MinLoopSeconds || loopSeconds > MaxLoopSeconds))
    {
      return ValidationResult.Invalid(
        StatusCodes.Status400BadRequest,
        "options.loopSeconds must be between 0.5 and 6.0 seconds."
      );
    }

    if (!string.IsNullOrWhiteSpace(options.MotionIntensity) &&
        options.MotionIntensity is not ("subtle" or "medium" or "high"))
    {
      return ValidationResult.Invalid(
        StatusCodes.Status400BadRequest,
        "options.motionIntensity must be subtle, medium, or high."
      );
    }

    if (!string.IsNullOrEmpty(options.StylePreset) && options.StylePreset.Length > 64)
    {
      return ValidationResult.Invalid(StatusCodes.Status400BadRequest, "options.stylePreset is too long.");
    }

    return ValidationResult.Valid;
  }

  private static ValidationResult ValidateProcessedSourceImage(SourceImageRequest sourceImage)
  {
    if (!string.Equals(sourceImage.MimeType, "image/jpeg", StringComparison.OrdinalIgnoreCase))
    {
      return ValidationResult.Invalid(
        StatusCodes.Status400BadRequest,
        "sourceImage must be a metadata-stripped JPEG image."
      );
    }

    if (sourceImage.Width is < 1 or > MaxProcessedImageDimension ||
        sourceImage.Height is < 1 or > MaxProcessedImageDimension)
    {
      return ValidationResult.Invalid(
        StatusCodes.Status400BadRequest,
        "sourceImage dimensions must be between 1 and 1024 pixels."
      );
    }

    byte[] bytes;
    try
    {
      bytes = Convert.FromBase64String(sourceImage.DataBase64);
    }
    catch (FormatException)
    {
      return ValidationResult.Invalid(StatusCodes.Status400BadRequest, "sourceImage data must be valid base64.");
    }

    if (bytes.Length > MaxProcessedImageBytes)
    {
      return ValidationResult.Invalid(
        StatusCodes.Status413PayloadTooLarge,
        "sourceImage exceeds the processed upload limit."
      );
    }

    return ValidationResult.Valid;
  }

  private static ValidationResult ValidateSourceImageContext(
    SourceImageRequest sourceImage,
    SourceImageContextRequest? sourceImageContext
  )
  {
    if (sourceImageContext is null)
    {
      return ValidationResult.Valid;
    }

    if (sourceImageContext.Width != sourceImage.Width || sourceImageContext.Height != sourceImage.Height)
    {
      return ValidationResult.Invalid(
        StatusCodes.Status400BadRequest,
        "sourceImageContext dimensions must match sourceImage dimensions."
      );
    }

    if (sourceImageContext.Width is < 1 or > MaxProcessedImageDimension ||
        sourceImageContext.Height is < 1 or > MaxProcessedImageDimension)
    {
      return ValidationResult.Invalid(
        StatusCodes.Status400BadRequest,
        "sourceImageContext dimensions must be between 1 and 1024 pixels."
      );
    }

    if (!string.IsNullOrEmpty(sourceImageContext.Summary) && sourceImageContext.Summary.Length > 240)
    {
      return ValidationResult.Invalid(
        StatusCodes.Status400BadRequest,
        "sourceImageContext summary is too long."
      );
    }

    if (!string.IsNullOrEmpty(sourceImageContext.Orientation) && sourceImageContext.Orientation.Length > 32)
    {
      return ValidationResult.Invalid(
        StatusCodes.Status400BadRequest,
        "sourceImageContext orientation is too long."
      );
    }

    if (!string.IsNullOrEmpty(sourceImageContext.AspectRatio) && sourceImageContext.AspectRatio.Length > 32)
    {
      return ValidationResult.Invalid(
        StatusCodes.Status400BadRequest,
        "sourceImageContext aspectRatio is too long."
      );
    }

    return ValidationResult.Valid;
  }
}

public readonly record struct ValidationResult(bool IsValid, int StatusCode, string Message)
{
  public static ValidationResult Valid { get; } = new(true, StatusCodes.Status200OK, string.Empty);
  public static ValidationResult Invalid(int statusCode, string message) => new(false, statusCode, message);
}
