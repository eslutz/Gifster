import Foundation
import Testing
@testable import GifsterCore

@Suite("User-facing errors")
struct UserFacingErrorTests {
  @Test("Moderation rejections use reviewable copy")
  func moderationRejectionsUseReviewableCopy() {
    let error = GifsterError.backendRejected(
      statusCode: 422,
      message: #"{"error":"invalid_request","message":"Request failed moderation checks."}"#
    )

    #expect(error.userFacingMessage == "That request cannot be generated. Try changing the prompt or selected image.")
  }

  @Test("Provider downtime uses retry copy")
  func providerDowntimeUsesRetryCopy() {
    let error = GifsterError.backendRejected(statusCode: 503, message: "provider unavailable")

    #expect(error.userFacingMessage == "The GIF generator is temporarily unavailable. Try again in a few minutes.")
  }

  @Test("Network failures use connection copy")
  func networkFailuresUseConnectionCopy() {
    let error = URLError(.notConnectedToInternet).gifsterUserFacingMessage

    #expect(error == "Gifster could not reach the backend. Check your connection and try again.")
  }

  @Test("App Attest unavailable explains physical-device requirement")
  func appAttestUnavailableExplainsPhysicalDeviceRequirement() {
    #expect(GifsterError.appAttestUnavailable.userFacingMessage == "This backend requires App Attest, which is not available here. Try again on a supported physical device.")
  }

  @Test("Local model unavailable message explains fallback")
  func localModelUnavailableExplainsFallback() {
    #expect(GifsterError.localModelUnavailable.userFacingMessage == "Local Apple models are unavailable, so Gifster will use its built-in prompt planner.")
  }
}
