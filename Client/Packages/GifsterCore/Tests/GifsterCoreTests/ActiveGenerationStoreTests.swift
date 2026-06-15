import Foundation
import Testing
@testable import GifsterCore

@Suite("Active generation store")
struct ActiveGenerationStoreTests {
  @Test("Active generation snapshot persists and reloads")
  func activeGenerationSnapshotPersistsAndReloads() async throws {
    let directory = try temporaryDirectory()
    let store = ActiveGenerationStore(directoryURL: directory)
    let snapshot = ActiveGenerationSnapshot(
      job: GenerationJob(
        id: "job-1",
        status: .queued,
        statusURL: URL(string: "https://example.test/jobs/job-1")!
      ),
      prompt: "cat in sunglasses",
      captionText: "cool cat",
      createdAt: Date(timeIntervalSince1970: 1_000)
    )

    try await store.save(snapshot)
    let restored = try await store.load(now: Date(timeIntervalSince1970: 1_100))

    #expect(restored == snapshot)
  }

  @Test("Clearing active generation removes the stored snapshot")
  func clearRemovesStoredSnapshot() async throws {
    let directory = try temporaryDirectory()
    let store = ActiveGenerationStore(directoryURL: directory)
    let snapshot = ActiveGenerationSnapshot(
      job: GenerationJob(
        id: "job-2",
        status: .running,
        statusURL: URL(string: "https://example.test/jobs/job-2")!
      ),
      prompt: "robot waving",
      captionText: nil,
      createdAt: Date(timeIntervalSince1970: 1_000)
    )

    try await store.save(snapshot)
    try await store.clear()
    let restored = try await store.load(now: Date(timeIntervalSince1970: 1_100))

    #expect(restored == nil)
  }

  @Test("Expired active generation is cleared and not restored")
  func expiredActiveGenerationIsClearedAndNotRestored() async throws {
    let directory = try temporaryDirectory()
    let store = ActiveGenerationStore(
      directoryURL: directory,
      expirationInterval: 60
    )
    let snapshot = ActiveGenerationSnapshot(
      job: GenerationJob(
        id: "job-3",
        status: .queued,
        statusURL: URL(string: "https://example.test/jobs/job-3")!
      ),
      prompt: "rocket launch",
      captionText: "go",
      createdAt: Date(timeIntervalSince1970: 1_000)
    )

    try await store.save(snapshot)
    let restored = try await store.load(now: Date(timeIntervalSince1970: 1_061))
    let secondLoad = try await store.load(now: Date(timeIntervalSince1970: 1_062))

    #expect(restored == nil)
    #expect(secondLoad == nil)
  }

  private func temporaryDirectory() throws -> URL {
    let url = FileManager.default.temporaryDirectory
      .appending(path: "GifsterCoreTests-\(UUID().uuidString)", directoryHint: .isDirectory)
    try FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
    return url
  }
}
