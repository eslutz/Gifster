using Gifster.Backend.Models;
using Gifster.Backend.Safety;

namespace Gifster.Backend.Tests;

public sealed class GenerationRequestPrivacyTests
{
  [Fact]
  public void SanitizeForJobStateClearsRawPromptCaptionAndImageBytes()
  {
    var request = TestGenerationRequests.Valid("raw original prompt") with
    {
      Mode = "image_to_gif",
      SourceImage = new SourceImageRequest(
        Convert.ToBase64String("source image bytes"u8.ToArray()),
        "image/jpeg",
        640,
        480
      ),
      SourceImageContext = new SourceImageContextRequest(
        640,
        480,
        "landscape",
        "4:3",
        "User-selected landscape JPEG source image, 640x480, aspect 4:3."
      ),
      Caption = new CaptionRequest("userText", "private caption")
    };

    var sanitized = GenerationRequestPrivacy.SanitizeForJobState(request);

    Assert.Null(sanitized.OriginalPrompt);
    Assert.Equal(request.CleanedPrompt, sanitized.CleanedPrompt);
    Assert.Equal(request.ExpandedPrompt, sanitized.ExpandedPrompt);
    Assert.Equal("userText", sanitized.Caption?.Mode);
    Assert.Null(sanitized.Caption?.Text);
    Assert.NotNull(sanitized.SourceImage);
    Assert.Equal(string.Empty, sanitized.SourceImage.DataBase64);
    Assert.Equal(640, sanitized.SourceImage.Width);
    Assert.Equal(480, sanitized.SourceImage.Height);
    Assert.Equal(request.SourceImageContext, sanitized.SourceImageContext);
  }
}
