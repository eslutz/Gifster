import Foundation

public enum GifsterError: Error, Equatable, LocalizedError, Sendable {
  case emptyPrompt
  case invalidCaption(reason: String)
  case invalidImage
  case backendRejected(statusCode: Int, message: String)
  case jobFailed(message: String)
  case jobTimedOut
  case mediaRenderingFailed(message: String)
  case storageFailed(message: String)

  public var errorDescription: String? {
    switch self {
    case .emptyPrompt:
      "Enter a prompt before generating a GIF."
    case let .invalidCaption(reason):
      reason
    case .invalidImage:
      "The selected image could not be processed."
    case let .backendRejected(_, message):
      message
    case let .jobFailed(message):
      message
    case .jobTimedOut:
      "The generation took too long. You can reopen Gifster and resume the job."
    case let .mediaRenderingFailed(message):
      message
    case let .storageFailed(message):
      message
    }
  }
}
