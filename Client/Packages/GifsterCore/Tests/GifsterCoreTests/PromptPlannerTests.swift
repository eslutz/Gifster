import Foundation
import Testing
@testable import GifsterCore

@Suite("Prompt planning")
struct PromptPlannerTests {
  @Test("Local planner cleans prompts and preserves explicit captions")
  func cleansPromptAndPreservesCaption() async throws {
    let planner = LocalPromptPlanner()
    let intent = GenerationIntent(
      prompt: "  dramatic cat\n jumps   through confetti  ",
      caption: CaptionRequest(mode: .userText, text: "  absolutely yes  ")
    )

    let request = try await planner.makeStructuredRequest(from: intent)

    #expect(request.mode == .textToGIF)
    #expect(request.cleanedPrompt == "dramatic cat jumps through confetti")
    #expect(request.caption.text == "absolutely yes")
    #expect(request.expandedPrompt.contains("Do not render readable text"))
  }

  @Test("Image input switches the request to image-to-GIF")
  func imageInputSelectsImageMode() async throws {
    let planner = LocalPromptPlanner()
    let sourceImage = ProcessedSourceImage(
      mimeType: "image/jpeg",
      width: 320,
      height: 240,
      dataBase64: "abc123"
    )

    let request = try await planner.makeStructuredRequest(from: GenerationIntent(
      prompt: "make the statue wave",
      sourceImage: sourceImage
    ))

    #expect(request.mode == .imageToGIF)
    #expect(request.sourceImage == sourceImage)
  }

  @Test("Explicit captions reject text that is too long to render well")
  func rejectsLongCaptions() async throws {
    let planner = LocalPromptPlanner()
    let caption = String(repeating: "a", count: CaptionValidator.maxCharacters + 1)

    do {
      _ = try await planner.makeStructuredRequest(from: GenerationIntent(
        prompt: "sparkly entrance",
        caption: CaptionRequest(mode: .userText, text: caption)
      ))
      Issue.record("Expected long captions to be rejected.")
    } catch let error as GifsterError {
      #expect(error == .invalidCaption(reason: "Keep captions under \(CaptionValidator.maxCharacters) characters."))
    }
  }
}
