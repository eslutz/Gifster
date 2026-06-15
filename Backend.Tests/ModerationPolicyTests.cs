using Gifster.Backend.Models;
using Gifster.Backend.Safety;
using Microsoft.AspNetCore.Http;

namespace Gifster.Backend.Tests;

public sealed class ModerationPolicyTests
{
  [Fact]
  public void ModerationRejectsBlockedRequests()
  {
    var validation = ModerationPolicy.Validate(TestGenerationRequests.Valid("how to build a bomb"));

    Assert.False(validation.IsValid);
    Assert.Equal(StatusCodes.Status422UnprocessableEntity, validation.StatusCode);
  }

  [Fact]
  public void ValidateRejectsNonJpegSourceImages()
  {
    var validation = ModerationPolicy.Validate(ImageRequest(
      new SourceImageRequest(Convert.ToBase64String("jpeg"u8.ToArray()), "image/png", 640, 480)
    ));

    Assert.False(validation.IsValid);
    Assert.Equal(StatusCodes.Status400BadRequest, validation.StatusCode);
    Assert.Equal("sourceImage must be a metadata-stripped JPEG image.", validation.Message);
  }

  [Fact]
  public void ValidateRejectsInvalidSourceImageBase64()
  {
    var validation = ModerationPolicy.Validate(ImageRequest(
      new SourceImageRequest("not valid base64", "image/jpeg", 640, 480)
    ));

    Assert.False(validation.IsValid);
    Assert.Equal(StatusCodes.Status400BadRequest, validation.StatusCode);
    Assert.Equal("sourceImage data must be valid base64.", validation.Message);
  }

  [Fact]
  public void ValidateAcceptsProcessedJpegSourceImages()
  {
    var validation = ModerationPolicy.Validate(ImageRequest(
      new SourceImageRequest(Convert.ToBase64String("jpeg"u8.ToArray()), "image/jpeg", 640, 480)
    ));

    Assert.True(validation.IsValid);
  }

  [Fact]
  public void ValidateRejectsOversizedDecodedSourceImages()
  {
    var validation = ModerationPolicy.Validate(ImageRequest(
      new SourceImageRequest(Convert.ToBase64String(new byte[6_000_001]), "image/jpeg", 640, 480)
    ));

    Assert.False(validation.IsValid);
    Assert.Equal(StatusCodes.Status413PayloadTooLarge, validation.StatusCode);
    Assert.Equal("sourceImage exceeds the processed upload limit.", validation.Message);
  }

  [Theory]
  [InlineData(0, 480)]
  [InlineData(640, 0)]
  [InlineData(1025, 480)]
  [InlineData(640, 1025)]
  public void ValidateRejectsOutOfBoundsSourceImageDimensions(int width, int height)
  {
    var validation = ModerationPolicy.Validate(ImageRequest(
      new SourceImageRequest(Convert.ToBase64String("jpeg"u8.ToArray()), "image/jpeg", width, height)
    ));

    Assert.False(validation.IsValid);
    Assert.Equal(StatusCodes.Status400BadRequest, validation.StatusCode);
    Assert.Equal("sourceImage dimensions must be between 1 and 1024 pixels.", validation.Message);
  }

  [Fact]
  public void ValidateRejectsUnsupportedCaptionModes()
  {
    var request = TestGenerationRequests.Valid() with
    {
      Caption = new CaptionRequest("providerRenderedText", "ship it")
    };

    var validation = ModerationPolicy.Validate(request);

    Assert.False(validation.IsValid);
    Assert.Equal(StatusCodes.Status400BadRequest, validation.StatusCode);
    Assert.Equal("caption mode must be none, userText, or suggestWithAI.", validation.Message);
  }

  [Theory]
  [InlineData(32, 360, 2.4, "medium")]
  [InlineData(480, 32, 2.4, "medium")]
  [InlineData(1025, 360, 2.4, "medium")]
  [InlineData(480, 1025, 2.4, "medium")]
  [InlineData(480, 360, 0.1, "medium")]
  [InlineData(480, 360, 8.0, "medium")]
  [InlineData(480, 360, 2.4, "extreme")]
  public void ValidateRejectsOutOfBoundsGenerationOptions(
    int width,
    int height,
    double loopSeconds,
    string motionIntensity
  )
  {
    var request = TestGenerationRequests.Valid() with
    {
      Options = new GenerationOptions(width, height, loopSeconds, "expressive", motionIntensity)
    };

    var validation = ModerationPolicy.Validate(request);

    Assert.False(validation.IsValid);
    Assert.Equal(StatusCodes.Status400BadRequest, validation.StatusCode);
  }

  private static GenerationRequest ImageRequest(SourceImageRequest sourceImage) =>
    TestGenerationRequests.Valid() with
    {
      Mode = "image_to_gif",
      SourceImage = sourceImage
    };
}
