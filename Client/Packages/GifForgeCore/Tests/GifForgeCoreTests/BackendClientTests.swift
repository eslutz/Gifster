import Foundation
import Testing
@testable import GifForgeCore

@Suite("Backend client")
struct BackendClientTests {
  @Test("Create job attaches bearer authorization when configured")
  func createJobAttachesBearerAuthorization() async throws {
    final class MockProtocol: URLProtocol {
      nonisolated(unsafe) static var capturedAuthorization: String?

      override class func canInit(with request: URLRequest) -> Bool { true }
      override class func canonicalRequest(for request: URLRequest) -> URLRequest { request }

      override func startLoading() {
        Self.capturedAuthorization = request.value(forHTTPHeaderField: "Authorization")
        let response = HTTPURLResponse(
          url: request.url!,
          statusCode: 202,
          httpVersion: nil,
          headerFields: ["Content-Type": "application/json"]
        )!
        let body = """
        {
          "jobId": "job-1",
          "status": "queued",
          "statusUrl": "https://example.test/v1/generations/job-1",
          "expiresAt": "2026-06-16T12:00:00Z",
          "requiredCredits": 5
        }
        """.data(using: .utf8)!

        client?.urlProtocol(self, didReceive: response, cacheStoragePolicy: .notAllowed)
        client?.urlProtocol(self, didLoad: body)
        client?.urlProtocolDidFinishLoading(self)
      }

      override func stopLoading() {}
    }

    let configuration = URLSessionConfiguration.ephemeral
    configuration.protocolClasses = [MockProtocol.self]
    let session = URLSession(configuration: configuration)
    let client = GifForgeBackendClient(
      baseURL: URL(string: "https://example.test")!,
      session: session,
      authorizer: StaticBearerTokenAuthorizer(token: "session-token")
    )

    let job = try await client.createJob(StructuredGenerationRequest(
      mode: .textToGIF,
      originalPrompt: "cat",
      cleanedPrompt: "cat",
      expandedPrompt: "cat",
      negativePrompt: "text",
      caption: CaptionRequest(mode: .none),
      sourceImage: nil,
      options: PromptStyleOptions()
    ))

    #expect(MockProtocol.capturedAuthorization == "Bearer session-token")
    #expect(job.expiresAt == "2026-06-16T12:00:00Z")
    #expect(job.requiredCredits == 5)
  }

  @Test("Job status preserves backend expiration timestamp")
  func jobStatusPreservesExpirationTimestamp() async throws {
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
          "jobId": "job-1",
          "status": "running",
          "downloadUrl": null,
          "message": null,
          "expiresAt": "2026-06-16T12:00:00.123Z"
        }
        """.data(using: .utf8)!

        client?.urlProtocol(self, didReceive: response, cacheStoragePolicy: .notAllowed)
        client?.urlProtocol(self, didLoad: body)
        client?.urlProtocolDidFinishLoading(self)
      }

      override func stopLoading() {}
    }

    let configuration = URLSessionConfiguration.ephemeral
    configuration.protocolClasses = [MockProtocol.self]
    let client = GifForgeBackendClient(
      baseURL: URL(string: "https://example.test")!,
      session: URLSession(configuration: configuration)
    )

    let job = try await client.fetchJobStatus(
      statusURL: URL(string: "https://example.test/v1/generations/job-1")!
    )

    #expect(job.expiresAt == "2026-06-16T12:00:00.123Z")
    #expect(job.expirationDate != nil)
  }

  @Test("Job status decodes retry metadata")
  func jobStatusDecodesRetryMetadata() async throws {
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
          "jobId": "job-1",
          "status": "failed",
          "downloadUrl": null,
          "message": "Generation provider reported failure.",
          "expiresAt": "2026-06-16T12:00:00.123Z",
          "retryAvailable": true,
          "retryReason": "provider_failed",
          "retryOfJobId": "job-1"
        }
        """.data(using: .utf8)!

        client?.urlProtocol(self, didReceive: response, cacheStoragePolicy: .notAllowed)
        client?.urlProtocol(self, didLoad: body)
        client?.urlProtocolDidFinishLoading(self)
      }

      override func stopLoading() {}
    }

    let configuration = URLSessionConfiguration.ephemeral
    configuration.protocolClasses = [MockProtocol.self]
    let client = GifForgeBackendClient(
      baseURL: URL(string: "https://example.test")!,
      session: URLSession(configuration: configuration)
    )

    let job = try await client.fetchJobStatus(
      statusURL: URL(string: "https://example.test/v1/generations/job-1")!
    )

    #expect(job.retryAvailable)
    #expect(job.retryReason == "provider_failed")
    #expect(job.retryOfJobId == "job-1")
  }

  @Test("App Attest challenge request decodes backend challenge")
  func appAttestChallengeDecodesBackendResponse() async throws {
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
          "challengeId": "challenge-1",
          "challenge": "abc123",
          "expiresAt": "2026-06-15T03:00:00Z"
        }
        """.data(using: .utf8)!

        client?.urlProtocol(self, didReceive: response, cacheStoragePolicy: .notAllowed)
        client?.urlProtocol(self, didLoad: body)
        client?.urlProtocolDidFinishLoading(self)
      }

      override func stopLoading() {}
    }

    let configuration = URLSessionConfiguration.ephemeral
    configuration.protocolClasses = [MockProtocol.self]
    let client = GifForgeBackendClient(
      baseURL: URL(string: "https://example.test")!,
      session: URLSession(configuration: configuration)
    )

    let challenge = try await client.createAppAttestChallenge()

    #expect(challenge.challengeID == "challenge-1")
    #expect(challenge.challenge == "abc123")
  }

  @Test("Download motion asset stores MP4 payloads locally")
  func downloadMotionAssetStoresMP4Payloads() async throws {
    final class MockProtocol: URLProtocol {
      override class func canInit(with request: URLRequest) -> Bool { true }
      override class func canonicalRequest(for request: URLRequest) -> URLRequest { request }

      override func startLoading() {
        let response = HTTPURLResponse(
          url: request.url!,
          statusCode: 200,
          httpVersion: nil,
          headerFields: ["Content-Type": "video/mp4"]
        )!
        let body = Data([0, 1, 2, 3, 4])

        client?.urlProtocol(self, didReceive: response, cacheStoragePolicy: .notAllowed)
        client?.urlProtocol(self, didLoad: body)
        client?.urlProtocolDidFinishLoading(self)
      }

      override func stopLoading() {}
    }

    let configuration = URLSessionConfiguration.ephemeral
    configuration.protocolClasses = [MockProtocol.self]
    let client = GifForgeBackendClient(
      baseURL: URL(string: "https://example.test")!,
      session: URLSession(configuration: configuration)
    )

    let asset = try await client.downloadMotionAsset(from: URL(string: "https://example.test/result")!)

    guard case let .mp4(url) = asset else {
      Issue.record("Expected MP4 motion asset.")
      return
    }
    let data = try Data(contentsOf: url)
    #expect(data == Data([0, 1, 2, 3, 4]))
  }
}
