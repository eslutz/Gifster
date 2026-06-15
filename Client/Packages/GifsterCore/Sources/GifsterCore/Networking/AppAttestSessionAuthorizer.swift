import Foundation

public protocol AppAttestSessionProviding: Sendable {
  func sessionToken() async throws -> String
}

public actor AppAttestSessionAuthorizer: BackendRequestAuthorizing {
  private let provider: any AppAttestSessionProviding
  private var cachedToken: String?

  public init(provider: any AppAttestSessionProviding) {
    self.provider = provider
  }

  public func authorizedRequest(for request: URLRequest) async throws -> URLRequest {
    let token: String
    if let cachedToken {
      token = cachedToken
    } else {
      let newToken = try await provider.sessionToken()
      cachedToken = newToken
      token = newToken
    }

    var request = request
    request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
    return request
  }
}

public struct StaticAppAttestSessionProvider: AppAttestSessionProviding {
  public var token: String

  public init(token: String) {
    self.token = token
  }

  public func sessionToken() async throws -> String {
    token
  }
}

#if os(iOS) && canImport(DeviceCheck) && canImport(CryptoKit)
import CryptoKit
import DeviceCheck

public actor DeviceCheckAppAttestSessionProvider: AppAttestSessionProviding {
  private let backendClient: GifsterBackendClient
  private let defaults: UserDefaults
  private let keyIDDefaultsKey: String

  public init(
    backendClient: GifsterBackendClient,
    defaults: UserDefaults = .standard,
    keyIDDefaultsKey: String = "gifsterAppAttestKeyID"
  ) {
    self.backendClient = backendClient
    self.defaults = defaults
    self.keyIDDefaultsKey = keyIDDefaultsKey
  }

  public func sessionToken() async throws -> String {
    let service = DCAppAttestService.shared
    guard service.isSupported else {
      throw GifsterError.appAttestUnavailable
    }

    let keyID = try await appAttestKeyID(service: service)
    let challenge = try await backendClient.createAppAttestChallenge()
    let challengeData = Data(challenge.challenge.utf8)
    let digest = Data(SHA256.hash(data: challengeData))
    let attestation = try await attestKey(service: service, keyID: keyID, clientDataHash: digest)
    let session = try await backendClient.createAppAttestSession(AppAttestAttestationRequest(
      keyID: keyID,
      challengeID: challenge.challengeID,
      attestationObject: attestation.base64EncodedString(),
      clientDataHash: digest.base64EncodedString()
    ))

    return session.sessionToken
  }

  private func appAttestKeyID(service: DCAppAttestService) async throws -> String {
    if let keyID = defaults.string(forKey: keyIDDefaultsKey), !keyID.isEmpty {
      return keyID
    }

    let keyID: String = try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<String, Error>) in
      service.generateKey { keyID, error in
        if let error {
          continuation.resume(throwing: error)
        } else if let keyID {
          continuation.resume(returning: keyID)
        } else {
          continuation.resume(throwing: GifsterError.appAttestUnavailable)
        }
      }
    }
    defaults.set(keyID, forKey: keyIDDefaultsKey)
    return keyID
  }

  private func attestKey(
    service: DCAppAttestService,
    keyID: String,
    clientDataHash: Data
  ) async throws -> Data {
    try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Data, Error>) in
      service.attestKey(keyID, clientDataHash: clientDataHash) { attestation, error in
        if let error {
          continuation.resume(throwing: error)
        } else if let attestation {
          continuation.resume(returning: attestation)
        } else {
          continuation.resume(throwing: GifsterError.appAttestUnavailable)
        }
      }
    }
  }
}
#endif
