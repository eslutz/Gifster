import Foundation

public struct GifsterBackendClient: @unchecked Sendable {
  public var baseURL: URL
  public var session: URLSession
  public var encoder: JSONEncoder
  public var decoder: JSONDecoder
  public var authorizer: any BackendRequestAuthorizing

  public init(
    baseURL: URL,
    session: URLSession = .shared,
    encoder: JSONEncoder = JSONEncoder(),
    decoder: JSONDecoder = JSONDecoder(),
    authorizer: any BackendRequestAuthorizing = NoopBackendRequestAuthorizer()
  ) {
    self.baseURL = baseURL
    self.session = session
    self.encoder = encoder
    self.decoder = decoder
    self.authorizer = authorizer
  }

  public func createJob(_ requestBody: StructuredGenerationRequest) async throws -> GenerationJob {
    var request = URLRequest(url: baseURL.appending(path: "/v1/generations"))
    request.httpMethod = "POST"
    request.setValue("application/json", forHTTPHeaderField: "Content-Type")
    request.httpBody = try encoder.encode(requestBody)

    let data = try await data(for: request)
    let response = try decoder.decode(JobSubmissionResponse.self, from: data)
    return GenerationJob(id: response.jobId, status: response.status, statusURL: response.statusUrl)
  }

  public func createAppAttestChallenge() async throws -> AppAttestChallenge {
    var request = URLRequest(url: baseURL.appending(path: "/v1/app-attest/challenges"))
    request.httpMethod = "POST"

    let data = try await data(for: request)
    return try decoder.decode(AppAttestChallenge.self, from: data)
  }

  public func createAppAttestSession(_ requestBody: AppAttestAttestationRequest) async throws -> AppAttestSession {
    var request = URLRequest(url: baseURL.appending(path: "/v1/app-attest/attestations"))
    request.httpMethod = "POST"
    request.setValue("application/json", forHTTPHeaderField: "Content-Type")
    request.httpBody = try encoder.encode(requestBody)

    let data = try await data(for: request)
    return try decoder.decode(AppAttestSession.self, from: data)
  }

  public func fetchJobStatus(statusURL: URL) async throws -> GenerationJob {
    let request = URLRequest(url: statusURL)
    let data = try await data(for: request)
    let response = try decoder.decode(JobStatusResponse.self, from: data)
    return GenerationJob(
      id: response.jobId,
      status: response.status,
      statusURL: statusURL,
      downloadURL: response.downloadUrl,
      message: response.message
    )
  }

  public func downloadFrameSequence(from url: URL) async throws -> FrameSequenceAsset {
    let request = URLRequest(url: url)
    let data = try await data(for: request)
    return try decoder.decode(FrameSequenceAsset.self, from: data)
  }

  public func downloadMotionAsset(from url: URL) async throws -> GeneratedMotionAsset {
    let request = URLRequest(url: url)
    let (data, response) = try await responseData(for: request)
    let contentType = response
      .value(forHTTPHeaderField: "Content-Type")?
      .lowercased() ?? ""

    if contentType.contains("video/mp4") {
      let destinationURL = FileManager.default.temporaryDirectory
        .appending(path: "Gifster-\(UUID().uuidString).mp4")
      try data.write(to: destinationURL, options: [.atomic])
      return .mp4(destinationURL)
    }

    return .frameSequence(try decoder.decode(FrameSequenceAsset.self, from: data))
  }

  private func data(for request: URLRequest) async throws -> Data {
    try await responseData(for: request).0
  }

  private func responseData(for request: URLRequest) async throws -> (Data, HTTPURLResponse) {
    let authorizedRequest = try await authorizer.authorizedRequest(for: request)
    let (data, response) = try await session.data(for: authorizedRequest)
    guard let http = response as? HTTPURLResponse else {
      throw GifsterError.backendRejected(statusCode: -1, message: "Backend did not return an HTTP response.")
    }

    guard (200..<300).contains(http.statusCode) else {
      let message = String(data: data, encoding: .utf8) ?? "Backend request failed."
      throw GifsterError.backendRejected(statusCode: http.statusCode, message: message)
    }

    return (data, http)
  }
}
