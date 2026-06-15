import Foundation

#if canImport(FoundationModels)
import FoundationModels
#endif

public struct FoundationModelsPromptPlanner: PromptPlanning {
  private let fallback: any PromptPlanning

  public init(fallback: any PromptPlanning = LocalPromptPlanner()) {
    self.fallback = fallback
  }

  public var canUseOnDeviceFoundationModels: Bool {
    #if canImport(FoundationModels)
    true
    #else
    false
    #endif
  }

  public func makeStructuredRequest(from intent: GenerationIntent) async throws -> StructuredGenerationRequest {
    #if canImport(FoundationModels)
    return try await fallback.makeStructuredRequest(from: intent)
    #else
    return try await fallback.makeStructuredRequest(from: intent)
    #endif
  }

  public func suggestCaptions(for request: StructuredGenerationRequest) async throws -> [CaptionSuggestion] {
    #if canImport(FoundationModels)
    return try await fallback.suggestCaptions(for: request)
    #else
    return try await fallback.suggestCaptions(for: request)
    #endif
  }
}
