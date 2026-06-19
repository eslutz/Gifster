import Foundation

public struct BackendAuthSession: Codable, Equatable, Sendable {
  public var userID: String
  public var appAccountToken: UUID
  public var accessToken: String
  public var accessTokenExpiresAt: String
  public var refreshToken: String
  public var refreshTokenExpiresAt: String

  enum CodingKeys: String, CodingKey {
    case userID = "userId"
    case appAccountToken
    case accessToken
    case accessTokenExpiresAt
    case refreshToken
    case refreshTokenExpiresAt
  }
}

public struct BackendAccountProfile: Codable, Equatable, Sendable {
  public var userID: String
  public var appAccountToken: UUID
  public var accountKind: String
  public var recoveryProvider: String?

  public var hasAppleRecovery: Bool {
    recoveryProvider == "apple"
  }

  enum CodingKeys: String, CodingKey {
    case userID = "userId"
    case appAccountToken
    case accountKind
    case recoveryProvider
  }
}

public struct BackendCreditBalance: Codable, Equatable, Sendable {
  public var grantedCredits: Int
  public var capturedDebits: Int
  public var reservedCredits: Int
  public var availableCredits: Int
}

public struct BackendIAPProduct: Codable, Equatable, Sendable, Identifiable {
  public var productID: String
  public var credits: Int
  public var active: Bool

  public var id: String { productID }

  enum CodingKeys: String, CodingKey {
    case productID = "productId"
    case credits
    case active
  }
}

public struct BackendIAPProductsResponse: Codable, Equatable, Sendable {
  public var products: [BackendIAPProduct]
}

public struct BackendIAPTransactionResult: Codable, Equatable, Sendable {
  public var transactionID: String
  public var productID: String
  public var grantedCredits: Int
  public var alreadyProcessed: Bool
  public var availableCredits: Int

  enum CodingKeys: String, CodingKey {
    case transactionID = "transactionId"
    case productID = "productId"
    case grantedCredits
    case alreadyProcessed
    case availableCredits
  }
}

struct AppleAuthRequest: Codable, Sendable {
  var identityToken: String
  var nonce: String?
}

struct IAPTransactionSubmissionRequest: Codable, Sendable {
  var productID: String
  var signedTransaction: String

  enum CodingKeys: String, CodingKey {
    case productID = "productId"
    case signedTransaction
  }
}
