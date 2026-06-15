import Foundation

public actor JobPollingService {
  private let client: GifsterBackendClient

  public init(client: GifsterBackendClient) {
    self.client = client
  }

  public func waitForCompletion(
    startingWith job: GenerationJob,
    timeoutSeconds: Double = 90,
    pollIntervalSeconds: Double = 1.5
  ) async throws -> GenerationJob {
    let deadline = Date().addingTimeInterval(timeoutSeconds)
    var current = job

    while Date() < deadline {
      current = try await client.fetchJobStatus(statusURL: current.statusURL)

      switch current.status {
      case .succeeded:
        return current
      case .failed:
        throw GifsterError.jobFailed(message: current.message ?? "Generation failed.")
      case .queued, .running:
        try await Task.sleep(nanoseconds: UInt64(pollIntervalSeconds * 1_000_000_000))
      }
    }

    throw GifsterError.jobTimedOut
  }
}
