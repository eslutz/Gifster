import Foundation

public struct AppAttestChallenge: Codable, Equatable, Sendable {
  public var challengeID: String
  public var challenge: String
  public var expiresAt: String

  public init(challengeID: String, challenge: String, expiresAt: String) {
    self.challengeID = challengeID
    self.challenge = challenge
    self.expiresAt = expiresAt
  }

  private enum CodingKeys: String, CodingKey {
    case challengeID = "challengeId"
    case challenge
    case expiresAt
  }
}

public struct AppAttestAttestationRequest: Codable, Equatable, Sendable {
  public var keyID: String
  public var challengeID: String
  public var attestationObject: String
  public var clientDataHash: String

  public init(keyID: String, challengeID: String, attestationObject: String, clientDataHash: String) {
    self.keyID = keyID
    self.challengeID = challengeID
    self.attestationObject = attestationObject
    self.clientDataHash = clientDataHash
  }

  private enum CodingKeys: String, CodingKey {
    case keyID = "keyId"
    case challengeID = "challengeId"
    case attestationObject
    case clientDataHash
  }
}

public struct AppAttestSession: Codable, Equatable, Sendable {
  public var sessionToken: String
  public var expiresAt: String

  public init(sessionToken: String, expiresAt: String) {
    self.sessionToken = sessionToken
    self.expiresAt = expiresAt
  }
}
