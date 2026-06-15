using Gifster.Backend.Models;

namespace Gifster.Backend.Tests;

internal static class TestGenerationRequests
{
  public static GenerationRequest Valid(string prompt = "cat in sunglasses") =>
    new(
      "96DD3998-C2E1-4C39-B7B1-3559D0D271C8",
      "text_to_gif",
      prompt,
      prompt,
      $"Create a short looping animated scene. Prompt: {prompt}. Do not render readable text.",
      "readable text, captions, subtitles",
      new CaptionRequest("none", null),
      null,
      new GenerationOptions(480, 360, 2.4, "expressive", "medium"),
      "test-trace"
    );
}
