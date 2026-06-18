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
    request.setValue(token, forHTTPHeaderField: "X-GifForge-App-Attest-Session")
    return request
  }
}

public struct SharedAppAttestSessionStore: @unchecked Sendable {
  private let defaults: UserDefaults
  private let tokenKey: String
  private let expiresAtKey: String

  public init(
    defaults: UserDefaults,
    tokenKey: String = "gifforgeAppAttestSessionToken",
    expiresAtKey: String = "gifforgeAppAttestSessionExpiresAt"
  ) {
    self.defaults = defaults
    self.tokenKey = tokenKey
    self.expiresAtKey = expiresAtKey
  }

  public func save(_ session: AppAttestSession) {
    defaults.set(session.sessionToken, forKey: tokenKey)
    defaults.set(session.expiresAt, forKey: expiresAtKey)
  }

  public func loadValidToken(now: Date = Date()) -> String? {
    guard let token = defaults.string(forKey: tokenKey), !token.isEmpty,
          let expiresAt = defaults.string(forKey: expiresAtKey),
          let expirationDate = Self.date(from: expiresAt),
          expirationDate > now.addingTimeInterval(300)
    else {
      return nil
    }

    return token
  }

  public func clear() {
    defaults.removeObject(forKey: tokenKey)
    defaults.removeObject(forKey: expiresAtKey)
  }

  private static func date(from value: String) -> Date? {
    let fractionalFormatter = ISO8601DateFormatter()
    fractionalFormatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
    if let date = fractionalFormatter.date(from: value) {
      return date
    }

    let standardFormatter = ISO8601DateFormatter()
    standardFormatter.formatOptions = [.withInternetDateTime]
    return standardFormatter.date(from: value)
  }
}

public struct AppAttestKeyIDStore: @unchecked Sendable {
  private let defaults: UserDefaults
  private let key: String

  public init(
    defaults: UserDefaults,
    key: String = "gifforgeAppAttestKeyID"
  ) {
    self.defaults = defaults
    self.key = key
  }

  public func load() -> String? {
    guard let keyID = defaults.string(forKey: key), !keyID.isEmpty else {
      return nil
    }

    return keyID
  }

  public func save(_ keyID: String) {
    defaults.set(keyID, forKey: key)
  }

  public func clear() {
    defaults.removeObject(forKey: key)
  }
}

public struct SharedAppAttestSessionProvider: AppAttestSessionProviding {
  private let store: SharedAppAttestSessionStore

  public init(store: SharedAppAttestSessionStore) {
    self.store = store
  }

  public func sessionToken() async throws -> String {
    guard let token = store.loadValidToken() else {
      throw GifForgeError.appAttestUnavailable
    }

    return token
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
  private let backendClient: GifForgeBackendClient
  private let keyIDStore: AppAttestKeyIDStore
  private let sessionStore: SharedAppAttestSessionStore?

  public init(
    backendClient: GifForgeBackendClient,
    defaults: UserDefaults = .standard,
    sessionStore: SharedAppAttestSessionStore? = nil
  ) {
    self.init(
      backendClient: backendClient,
      keyIDStore: AppAttestKeyIDStore(defaults: defaults),
      sessionStore: sessionStore
    )
  }

  public init(
    backendClient: GifForgeBackendClient,
    keyIDStore: AppAttestKeyIDStore,
    sessionStore: SharedAppAttestSessionStore? = nil
  ) {
    self.backendClient = backendClient
    self.keyIDStore = keyIDStore
    self.sessionStore = sessionStore
  }

  public func sessionToken() async throws -> String {
    if let token = sessionStore?.loadValidToken() {
      return token
    }

    let service = DCAppAttestService.shared
    guard service.isSupported else {
      throw GifForgeError.appAttestUnavailable
    }

    let keyID = try await appAttestKeyID(service: service, allowStoredKey: true)
    do {
      return try await createSessionToken(service: service, keyID: keyID)
    } catch {
      keyIDStore.clear()
      let freshKeyID = try await appAttestKeyID(service: service, allowStoredKey: false)
      return try await createSessionToken(service: service, keyID: freshKeyID)
    }
  }

  private func createSessionToken(service: DCAppAttestService, keyID: String) async throws -> String {
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
    sessionStore?.save(session)

    return session.sessionToken
  }

  private func appAttestKeyID(service: DCAppAttestService, allowStoredKey: Bool) async throws -> String {
    if allowStoredKey,
       let keyID = keyIDStore.load() {
      return keyID
    }

    let keyID: String = try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<String, Error>) in
      service.generateKey { keyID, error in
        if let error {
          continuation.resume(throwing: error)
        } else if let keyID {
          continuation.resume(returning: keyID)
        } else {
          continuation.resume(throwing: GifForgeError.appAttestUnavailable)
        }
      }
    }
    keyIDStore.save(keyID)
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
          continuation.resume(throwing: GifForgeError.appAttestUnavailable)
        }
      }
    }
  }
}
#endif
