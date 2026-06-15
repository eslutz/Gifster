import Foundation

#if canImport(FoundationModels)
import FoundationModels

@Generable
@available(iOS 26.0, macOS 26.0, *)
private struct FoundationGenerationPlan {
  @Guide(description: "A concise cleaned-up GIF prompt with no line breaks.")
  var cleanedPrompt: String

  @Guide(description: "A vivid expanded motion prompt for an AI video provider. Do not ask the provider to render readable text.")
  var expandedPrompt: String

  @Guide(description: "A short negative prompt that prevents readable text, captions, logos, watermarks, and unsafe details.")
  var negativePrompt: String
}

@Generable
@available(iOS 26.0, macOS 26.0, *)
private struct FoundationCaptionPlan {
  @Guide(description: "A short caption under 64 characters.")
  var first: String

  @Guide(description: "A second short caption under 64 characters.")
  var second: String

  @Guide(description: "A third short caption under 64 characters.")
  var third: String
}
#endif

public struct FoundationModelsPromptPlanner: PromptPlanning {
  private let fallback: any PromptPlanning

  public init(fallback: any PromptPlanning = LocalPromptPlanner()) {
    self.fallback = fallback
  }

  public var canUseOnDeviceFoundationModels: Bool {
    #if canImport(FoundationModels)
    if #available(iOS 26.0, macOS 26.0, *) {
      return SystemLanguageModel.default.isAvailable
    }

    return false
    #else
    false
    #endif
  }

  public func makeStructuredRequest(from intent: GenerationIntent) async throws -> StructuredGenerationRequest {
    #if canImport(FoundationModels)
    guard #available(iOS 26.0, macOS 26.0, *), canUseOnDeviceFoundationModels else {
      return try await fallback.makeStructuredRequest(from: intent)
    }

    return try await makeFoundationStructuredRequest(from: intent)
    #else
    return try await fallback.makeStructuredRequest(from: intent)
    #endif
  }

  public func suggestCaptions(for request: StructuredGenerationRequest) async throws -> [CaptionSuggestion] {
    #if canImport(FoundationModels)
    guard #available(iOS 26.0, macOS 26.0, *), canUseOnDeviceFoundationModels else {
      return try await fallback.suggestCaptions(for: request)
    }

    return try await makeFoundationCaptionSuggestions(for: request)
    #else
    return try await fallback.suggestCaptions(for: request)
    #endif
  }

  #if canImport(FoundationModels)
  @available(iOS 26.0, macOS 26.0, *)
  private func makeFoundationStructuredRequest(from intent: GenerationIntent) async throws -> StructuredGenerationRequest {
    do {
      let session = LanguageModelSession()
      let response = try await session.respond(
        to: generationPrompt(for: intent, mode: intent.sourceImage == nil ? .textToGIF : .imageToGIF),
        generating: FoundationGenerationPlan.self
      )
      let plan = response.content
      let mode: GenerationMode = intent.sourceImage == nil ? .textToGIF : .imageToGIF
      let caption = try normalizeCaption(intent.caption)
      let cleaned = clean(plan.cleanedPrompt)
      guard !cleaned.isEmpty else {
        return try await fallback.makeStructuredRequest(from: intent)
      }

      return StructuredGenerationRequest(
        mode: mode,
        originalPrompt: intent.prompt,
        cleanedPrompt: cleaned,
        expandedPrompt: clean(plan.expandedPrompt),
        negativePrompt: clean(plan.negativePrompt),
        caption: caption,
        sourceImage: intent.sourceImage,
        options: intent.options
      )
    } catch {
      return try await fallback.makeStructuredRequest(from: intent)
    }
  }

  @available(iOS 26.0, macOS 26.0, *)
  private func makeFoundationCaptionSuggestions(for request: StructuredGenerationRequest) async throws -> [CaptionSuggestion] {
    do {
      let session = LanguageModelSession()
      let response = try await session.respond(
        to: """
        Suggest three brief, punchy captions for this generated GIF.
        Keep every caption under \(CaptionValidator.maxCharacters) characters.
        Prompt: \(request.cleanedPrompt)
        Do not include hashtags, emojis, profanity, or quoted dialogue.
        """,
        generating: FoundationCaptionPlan.self
      )

      let plan = response.content
      let candidates = [plan.first, plan.second, plan.third]
        .map { clean($0) }
        .filter { !$0.isEmpty }
        .map { String($0.prefix(CaptionValidator.maxCharacters)) }

      if candidates.isEmpty {
        return try await fallback.suggestCaptions(for: request)
      }

      return candidates.map { CaptionSuggestion(text: $0) }
    } catch {
      return try await fallback.suggestCaptions(for: request)
    }
  }
  #endif

  private func clean(_ text: String) -> String {
    text
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

  private func generationPrompt(for intent: GenerationIntent, mode: GenerationMode) -> String {
    let source = mode == .imageToGIF
      ? "The user selected a static source image. Plan how to animate that image."
      : "The user wants a text-to-GIF animation."

    return """
    \(source)
    Convert the user's messy request into a provider-ready animation plan.
    Preserve the user's intent. Keep visible readable text out of the provider prompt because captions are rendered locally by the app.
    Original prompt: \(intent.prompt)
    Style preset: \(intent.options.stylePreset)
    Motion intensity: \(intent.options.motionIntensity.rawValue)
    Loop duration: \(String(format: "%.1f", intent.options.loopSeconds)) seconds
    """
  }
}
