import Foundation

public protocol BackendRequestAuthorizing: Sendable {
  func authorizedRequest(for request: URLRequest) async throws -> URLRequest
}

public struct NoopBackendRequestAuthorizer: BackendRequestAuthorizing {
  public init() {}

  public func authorizedRequest(for request: URLRequest) async throws -> URLRequest {
    request
  }
}

public struct StaticBearerTokenAuthorizer: BackendRequestAuthorizing {
  public var token: String

  public init(token: String) {
    self.token = token
  }

  public func authorizedRequest(for request: URLRequest) async throws -> URLRequest {
    var request = request
    request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
    return request
  }
}
