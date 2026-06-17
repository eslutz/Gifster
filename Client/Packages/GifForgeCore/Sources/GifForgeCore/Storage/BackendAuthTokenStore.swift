import Foundation
import Security

public struct BackendAuthTokenSnapshot: Codable, Equatable, Sendable {
  public var accessToken: String
  public var accessTokenExpiresAt: String
  public var refreshToken: String
  public var refreshTokenExpiresAt: String

  public init(session: BackendAuthSession) {
    accessToken = session.accessToken
    accessTokenExpiresAt = session.accessTokenExpiresAt
    refreshToken = session.refreshToken
    refreshTokenExpiresAt = session.refreshTokenExpiresAt
  }
}

public struct KeychainBackendAuthTokenStore: BackendAccessTokenProviding, @unchecked Sendable {
  private let service: String
  private let account: String
  private let accessGroup: String?
  private let encoder = JSONEncoder()
  private let decoder = JSONDecoder()

  public init(
    service: String = "dev.ericslutz.gifforge.backend-auth",
    account: String = "default",
    accessGroup: String? = AppStorageDirectories.keychainAccessGroup
  ) {
    self.service = service
    self.account = account
    self.accessGroup = accessGroup
  }

  public func save(session: BackendAuthSession) throws {
    let data = try encoder.encode(BackendAuthTokenSnapshot(session: session))
    var query = baseQuery()
    SecItemDelete(query as CFDictionary)

    query[kSecValueData as String] = data
    query[kSecAttrAccessible as String] = kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly
    let status = SecItemAdd(query as CFDictionary, nil)
    guard status == errSecSuccess else {
      throw GifForgeError.backendRejected(statusCode: Int(status), message: "Could not save backend credentials.")
    }
  }

  public func load() throws -> BackendAuthTokenSnapshot? {
    var query = baseQuery()
    query[kSecReturnData as String] = true
    query[kSecMatchLimit as String] = kSecMatchLimitOne

    var item: CFTypeRef?
    let status = SecItemCopyMatching(query as CFDictionary, &item)
    if status == errSecItemNotFound {
      return nil
    }
    guard status == errSecSuccess, let data = item as? Data else {
      throw GifForgeError.backendRejected(statusCode: Int(status), message: "Could not load backend credentials.")
    }
    return try decoder.decode(BackendAuthTokenSnapshot.self, from: data)
  }

  public func clear() {
    SecItemDelete(baseQuery() as CFDictionary)
  }

  public func accessToken() async throws -> String? {
    try load()?.accessToken
  }

  private func baseQuery() -> [String: Any] {
    var query: [String: Any] = [
      kSecClass as String: kSecClassGenericPassword,
      kSecAttrService as String: service,
      kSecAttrAccount as String: account
    ]
    if let accessGroup, !accessGroup.isEmpty {
      query[kSecAttrAccessGroup as String] = accessGroup
    }
    return query
  }
}
