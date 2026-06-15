import Foundation

public protocol PromptPlanning: Sendable {
  func makeStructuredRequest(from intent: GenerationIntent) async throws -> StructuredGenerationRequest
  func suggestCaptions(for request: StructuredGenerationRequest) async throws -> [CaptionSuggestion]
}

public struct PromptPlannerFactory {
  public static func makeDefaultPlanner() -> any PromptPlanning {
    FoundationModelsPromptPlanner(fallback: LocalPromptPlanner())
  }
}
