import Foundation

public struct GifsterBackendClient: @unchecked Sendable {
  public var baseURL: URL
  public var session: URLSession
  public var encoder: JSONEncoder
  public var decoder: JSONDecoder

  public init(
    baseURL: URL,
    session: URLSession = .shared,
    encoder: JSONEncoder = JSONEncoder(),
    decoder: JSONDecoder = JSONDecoder()
  ) {
    self.baseURL = baseURL
    self.session = session
    self.encoder = encoder
    self.decoder = decoder
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

  private func data(for request: URLRequest) async throws -> Data {
    let (data, response) = try await session.data(for: request)
    guard let http = response as? HTTPURLResponse else {
      return data
    }

    guard (200..<300).contains(http.statusCode) else {
      let message = String(data: data, encoding: .utf8) ?? "Backend request failed."
      throw GifsterError.backendRejected(statusCode: http.statusCode, message: message)
    }

    return data
  }
}
