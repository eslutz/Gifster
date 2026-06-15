import Foundation

public enum CaptionMode: String, Codable, CaseIterable, Identifiable, Sendable {
  case none
  case userText
  case suggestWithAI

  public var id: String { rawValue }

  public var displayName: String {
    switch self {
    case .none:
      "No caption"
    case .userText:
      "Use my text"
    case .suggestWithAI:
      "Suggest text"
    }
  }
}

public struct CaptionRequest: Codable, Equatable, Sendable {
  public var mode: CaptionMode
  public var text: String?

  public init(mode: CaptionMode, text: String? = nil) {
    self.mode = mode
    self.text = text
  }
}

public struct CaptionSuggestion: Codable, Equatable, Identifiable, Sendable {
  public var id: UUID
  public var text: String

  public init(id: UUID = UUID(), text: String) {
    self.id = id
    self.text = text
  }
}

public enum CaptionValidator {
  public static let maxCharacters = 64

  public static func normalizedExplicitCaption(_ caption: String) throws -> String {
    let cleaned = caption
      .replacingOccurrences(of: "\n", with: " ")
      .trimmingCharacters(in: .whitespacesAndNewlines)

    guard cleaned.count <= maxCharacters else {
      throw GifsterError.invalidCaption(reason: "Keep captions under \(maxCharacters) characters.")
    }

    return cleaned
  }
}
