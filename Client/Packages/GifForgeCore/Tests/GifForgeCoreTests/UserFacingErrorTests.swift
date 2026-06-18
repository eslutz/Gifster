import Foundation
import Testing
@testable import GifForgeCore

@Suite("User-facing errors")
struct UserFacingErrorTests {
  @Test("Moderation rejections use reviewable copy")
  func moderationRejectionsUseReviewableCopy() {
    let error = GifForgeError.backendRejected(
      statusCode: 422,
      message: #"{"error":"invalid_request","message":"Request failed moderation checks."}"#
    )

    #expect(error.userFacingMessage == "That request cannot be generated. Try changing the prompt or selected image.")
  }

  @Test("Provider downtime uses retry copy")
  func providerDowntimeUsesRetryCopy() {
    let error = GifForgeError.backendRejected(statusCode: 503, message: "provider unavailable")

    #expect(error.userFacingMessage == "The GIF generator is temporarily unavailable. Try again in a few minutes.")
  }

  @Test("Network failures use connection copy")
  func networkFailuresUseConnectionCopy() {
    let error = URLError(.notConnectedToInternet).gifforgeUserFacingMessage

    #expect(error == "GifForge could not reach the backend. Check your connection and try again.")
  }

  @Test("App Attest unavailable explains physical-device requirement")
  func appAttestUnavailableExplainsPhysicalDeviceRequirement() {
    #expect(GifForgeError.appAttestUnavailable.userFacingMessage == "This backend requires App Attest, which is not available here. Try again on a supported physical device.")
  }

  @Test("Apple sign-in failures use auth-specific copy")
  func appleSignInFailuresUseAuthSpecificCopy() {
    let error = GifForgeError.appleSignInFailed(message: "The backend rejected the Apple identity token.")

    #expect(error.userFacingMessage == "The backend rejected the Apple identity token.")
  }

  @Test("Local model unavailable message explains fallback")
  func localModelUnavailableExplainsFallback() {
    #expect(GifForgeError.localModelUnavailable.userFacingMessage == "Local Apple models are unavailable, so GifForge will use its built-in prompt planner.")
  }
}
