import Foundation

public struct LocalPromptPlanner: PromptPlanning {
  public init() {}

  public func makeStructuredRequest(from intent: GenerationIntent) async throws -> StructuredGenerationRequest {
    let cleaned = cleanPrompt(intent.prompt)
    guard !cleaned.isEmpty else {
      throw GifsterError.emptyPrompt
    }

    let caption = try normalizeCaption(intent.caption)
    let mode: GenerationMode = intent.sourceImage == nil ? .textToGIF : .imageToGIF
    let sourceImageContext = intent.sourceImage.map(SourceImageContext.init(sourceImage:))
    let expansion = expandedPrompt(
      cleaned,
      mode: mode,
      sourceImageContext: sourceImageContext,
      options: intent.options
    )

    return StructuredGenerationRequest(
      mode: mode,
      originalPrompt: intent.prompt,
      cleanedPrompt: cleaned,
      expandedPrompt: expansion,
      negativePrompt: "readable text, captions, subtitles, logos, watermarks, gore, hate symbols",
      caption: caption,
      sourceImage: intent.sourceImage,
      sourceImageContext: sourceImageContext,
      options: intent.options
    )
  }

  public func suggestCaptions(for request: StructuredGenerationRequest) async throws -> [CaptionSuggestion] {
    let base = request.cleanedPrompt
    let shortened = base
      .split(separator: " ")
      .prefix(5)
      .joined(separator: " ")
      .capitalized

    let candidates = [
      shortened.isEmpty ? "Mood achieved" : shortened,
      "When the prompt hits",
      "Generated for this exact moment"
    ]

    return candidates
      .map { String($0.prefix(CaptionValidator.maxCharacters)) }
      .map { CaptionSuggestion(text: $0) }
  }

  private func cleanPrompt(_ prompt: String) -> String {
    prompt
      .replacingOccurrences(of: "\n", with: " ")
      .split(separator: " ")
      .joined(separator: " ")
      .trimmingCharacters(in: .whitespacesAndNewlines)
  }

  private func normalizeCaption(_ caption: CaptionRequest) throws -> CaptionRequest {
    switch caption.mode {
    case .none:
      CaptionRequest(mode: .none)
    case .userText:
      CaptionRequest(mode: .userText, text: try CaptionValidator.normalizedExplicitCaption(caption.text ?? ""))
    case .suggestWithAI:
      CaptionRequest(mode: .suggestWithAI, text: caption.text)
    }
  }

  private func expandedPrompt(
    _ prompt: String,
    mode: GenerationMode,
    sourceImageContext: SourceImageContext?,
    options: PromptStyleOptions
  ) -> String {
    let source = mode == .imageToGIF
      ? "Animate the user-selected image. \(sourceImageContext?.summary ?? "")"
      : "Create a short looping animated scene."

    return [
      source,
      "Prompt: \(prompt).",
      "Style preset: \(options.stylePreset).",
      "Motion: \(options.motionIntensity.rawValue).",
      "Duration: \(String(format: "%.1f", options.loopSeconds)) seconds.",
      "Do not render readable text in the animation."
    ].joined(separator: " ")
  }
}
