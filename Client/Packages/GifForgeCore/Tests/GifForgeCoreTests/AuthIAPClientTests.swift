import Foundation
import Testing
@testable import GifForgeCore

@Suite("Auth and IAP client")
struct AuthIAPClientTests {
  @Test("Sign in with Apple decodes backend auth session")
  func signInWithAppleDecodesBackendAuthSession() async throws {
    final class MockProtocol: URLProtocol {
      override class func canInit(with request: URLRequest) -> Bool { true }
      override class func canonicalRequest(for request: URLRequest) -> URLRequest { request }

      override func startLoading() {
        let response = HTTPURLResponse(
          url: request.url!,
          statusCode: 200,
          httpVersion: nil,
          headerFields: ["Content-Type": "application/json"]
        )!
        let body = """
        {
          "userId": "user-1",
          "appAccountToken": "00000000-0000-0000-0000-000000000001",
          "accessToken": "access-token",
          "accessTokenExpiresAt": "2026-06-16T23:00:00Z",
          "refreshToken": "refresh-token",
          "refreshTokenExpiresAt": "2026-07-16T23:00:00Z"
        }
        """.data(using: .utf8)!

        client?.urlProtocol(self, didReceive: response, cacheStoragePolicy: .notAllowed)
        client?.urlProtocol(self, didLoad: body)
        client?.urlProtocolDidFinishLoading(self)
      }

      override func stopLoading() {}
    }

    let client = GifForgeBackendClient(
      baseURL: URL(string: "https://example.test")!,
      session: URLSession(configuration: configuration(MockProtocol.self))
    )

    let session = try await client.signInWithApple(identityToken: "identity-token", nonce: "nonce")

    #expect(session.userID == "user-1")
    #expect(session.appAccountToken == UUID(uuidString: "00000000-0000-0000-0000-000000000001"))
    #expect(session.accessToken == "access-token")
    #expect(session.refreshToken == "refresh-token")
  }

  @Test("Sign in with Apple backend rejection uses auth-specific error")
  func signInWithAppleBackendRejectionUsesAuthSpecificError() async throws {
    final class MockProtocol: URLProtocol {
      override class func canInit(with request: URLRequest) -> Bool { true }
      override class func canonicalRequest(for request: URLRequest) -> URLRequest { request }

      override func startLoading() {
        let response = HTTPURLResponse(
          url: request.url!,
          statusCode: 401,
          httpVersion: nil,
          headerFields: ["Content-Type": "application/json"]
        )!
        let body = #"{"error":"unauthorized","message":"Authentication failed."}"#.data(using: .utf8)!

        client?.urlProtocol(self, didReceive: response, cacheStoragePolicy: .notAllowed)
        client?.urlProtocol(self, didLoad: body)
        client?.urlProtocolDidFinishLoading(self)
      }

      override func stopLoading() {}
    }

    let client = GifForgeBackendClient(
      baseURL: URL(string: "https://example.test")!,
      session: URLSession(configuration: configuration(MockProtocol.self))
    )

    do {
      _ = try await client.signInWithApple(identityToken: "identity-token", nonce: "nonce")
      Issue.record("Expected sign-in to throw an auth-specific error.")
    } catch let error as GifForgeError {
      #expect(error == .appleSignInFailed(message: "GifForge could not verify your Apple sign-in with the backend. Try again."))
    }
  }

  @Test("IAP transaction submission attaches bearer auth and encodes signed transaction")
  func iapTransactionSubmissionAttachesBearerAndEncodesPayload() async throws {
    final class MockProtocol: URLProtocol {
      nonisolated(unsafe) static var capturedAuthorization: String?
      nonisolated(unsafe) static var capturedBody: String?

      override class func canInit(with request: URLRequest) -> Bool { true }
      override class func canonicalRequest(for request: URLRequest) -> URLRequest { request }

      override func startLoading() {
        Self.capturedAuthorization = request.value(forHTTPHeaderField: "Authorization")
        Self.capturedBody = requestBodyString(request)
        let response = HTTPURLResponse(
          url: request.url!,
          statusCode: 200,
          httpVersion: nil,
          headerFields: ["Content-Type": "application/json"]
        )!
        let body = """
        {
          "transactionId": "tx-1",
          "productId": "dev.ericslutz.gifforge.credits.10",
          "grantedCredits": 10,
          "alreadyProcessed": false,
          "availableCredits": 10
        }
        """.data(using: .utf8)!

        client?.urlProtocol(self, didReceive: response, cacheStoragePolicy: .notAllowed)
        client?.urlProtocol(self, didLoad: body)
        client?.urlProtocolDidFinishLoading(self)
      }

      override func stopLoading() {}

      private func requestBodyString(_ request: URLRequest) -> String? {
        if let body = request.httpBody {
          return String(data: body, encoding: .utf8)
        }
        guard let stream = request.httpBodyStream else {
          return nil
        }

        stream.open()
        defer { stream.close() }
        var data = Data()
        let bufferSize = 1024
        let buffer = UnsafeMutablePointer<UInt8>.allocate(capacity: bufferSize)
        defer { buffer.deallocate() }
        while stream.hasBytesAvailable {
          let count = stream.read(buffer, maxLength: bufferSize)
          if count <= 0 {
            break
          }
          data.append(buffer, count: count)
        }
        return String(data: data, encoding: .utf8)
      }
    }

    let client = GifForgeBackendClient(
      baseURL: URL(string: "https://example.test")!,
      session: URLSession(configuration: configuration(MockProtocol.self)),
      authorizer: StaticBearerTokenAuthorizer(token: "access-token")
    )

    let result = try await client.submitIAPTransaction(
      productID: "dev.ericslutz.gifforge.credits.10",
      signedTransaction: "signed-jws"
    )

    #expect(MockProtocol.capturedAuthorization == "Bearer access-token")
    #expect(MockProtocol.capturedBody?.contains("\"signedTransaction\":\"signed-jws\"") == true)
    #expect(result.availableCredits == 10)
    #expect(result.grantedCredits == 10)
  }

  @Test("Composite authorizer keeps backend bearer auth and App Attest session header")
  func compositeAuthorizerKeepsBearerAndAppAttestHeader() async throws {
    let authorizer = CompositeBackendRequestAuthorizer(authorizers: [
      StaticBearerTokenAuthorizer(token: "access-token"),
      AppAttestSessionAuthorizer(provider: StaticAppAttestSessionProvider(token: "app-attest-token"))
    ])
    let request = try await authorizer.authorizedRequest(
      for: URLRequest(url: URL(string: "https://example.test/v1/generations")!)
    )

    #expect(request.value(forHTTPHeaderField: "Authorization") == "Bearer access-token")
    #expect(request.value(forHTTPHeaderField: "X-GifForge-App-Attest-Session") == "app-attest-token")
  }

  private func configuration(_ protocolClass: URLProtocol.Type) -> URLSessionConfiguration {
    let configuration = URLSessionConfiguration.ephemeral
    configuration.protocolClasses = [protocolClass]
    return configuration
  }
}
