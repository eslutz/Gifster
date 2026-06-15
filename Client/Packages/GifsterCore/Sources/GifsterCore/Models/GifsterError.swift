import Foundation

public enum GifsterError: Error, Equatable, LocalizedError, Sendable {
  case emptyPrompt
  case invalidCaption(reason: String)
  case invalidImage
  case backendRejected(statusCode: Int, message: String)
  case appAttestUnavailable
  case localModelUnavailable
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
    case .backendRejected:
      userFacingMessage
    case .appAttestUnavailable:
      userFacingMessage
    case .localModelUnavailable:
      userFacingMessage
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

  public var userFacingMessage: String {
    switch self {
    case .emptyPrompt:
      "Enter a prompt before generating a GIF."
    case let .invalidCaption(reason):
      reason
    case .invalidImage:
      "The selected image could not be processed. Try a different image."
    case let .backendRejected(statusCode, message):
      BackendErrorMessage(statusCode: statusCode, rawMessage: message).userFacingMessage
    case .appAttestUnavailable:
      "This backend requires App Attest, which is not available here. Try again on a supported physical device."
    case .localModelUnavailable:
      "Local Apple models are unavailable, so Gifster will use its built-in prompt planner."
    case let .jobFailed(message):
      message.isEmpty ? "The GIF generation failed. Try again with a different prompt." : message
    case .jobTimedOut:
      "The generation took too long. You can reopen Gifster and resume the job."
    case let .mediaRenderingFailed(message):
      message
    case let .storageFailed(message):
      message
    }
  }
}

private struct BackendErrorMessage {
  var statusCode: Int
  var rawMessage: String

  var userFacingMessage: String {
    if statusCode == -1 {
      return "Gifster could not read the backend response. Try again."
    }

    if statusCode == 401 || statusCode == 403 {
      return "Gifster could not verify this app with the backend. Try again on a supported physical device."
    }

    if statusCode == 408 || statusCode == 429 || statusCode >= 500 {
      return "The GIF generator is temporarily unavailable. Try again in a few minutes."
    }

    if statusCode == 413 {
      return "That image or request is too large. Try a smaller image or shorter prompt."
    }

    let normalized = rawMessage.lowercased()
    if statusCode == 422 ||
        normalized.contains("moderation") ||
        normalized.contains("safety") {
      return "That request cannot be generated. Try changing the prompt or selected image."
    }

    if !rawMessage.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
      return rawMessage
    }

    return "Gifster could not complete that request. Try again."
  }
}

public extension Error {
  var gifsterUserFacingMessage: String {
    if let gifsterError = self as? GifsterError {
      return gifsterError.userFacingMessage
    }

    if let urlError = self as? URLError {
      return urlError.gifsterUserFacingMessage
    }

    return localizedDescription
  }
}

public extension URLError {
  var gifsterUserFacingMessage: String {
    switch code {
    case .notConnectedToInternet, .networkConnectionLost, .cannotFindHost, .cannotConnectToHost, .timedOut:
      "Gifster could not reach the backend. Check your connection and try again."
    case .secureConnectionFailed, .serverCertificateHasBadDate, .serverCertificateUntrusted, .serverCertificateHasUnknownRoot, .serverCertificateNotYetValid:
      "Gifster could not establish a secure connection to the backend."
    default:
      "Gifster could not reach the backend. Try again."
    }
  }
}
