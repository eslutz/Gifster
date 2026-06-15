import Foundation

public struct ActiveGenerationSnapshot: Codable, Equatable, Sendable {
  public var job: GenerationJob
  public var prompt: String
  public var captionText: String?
  public var createdAt: Date

  public init(
    job: GenerationJob,
    prompt: String,
    captionText: String?,
    createdAt: Date = Date()
  ) {
    self.job = job
    self.prompt = prompt
    self.captionText = captionText
    self.createdAt = createdAt
  }
}

public actor ActiveGenerationStore {
  private let fileURL: URL
  private let expirationInterval: TimeInterval
  private let encoder: JSONEncoder
  private let decoder: JSONDecoder

  public init(
    directoryURL: URL,
    expirationInterval: TimeInterval = 6 * 60 * 60
  ) {
    self.fileURL = directoryURL.appending(path: "active-generation.json")
    self.expirationInterval = expirationInterval
    self.encoder = JSONEncoder()
    self.decoder = JSONDecoder()
    self.encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
    self.encoder.dateEncodingStrategy = .iso8601
    self.decoder.dateDecodingStrategy = .iso8601
  }

  public func load(now: Date = Date()) throws -> ActiveGenerationSnapshot? {
    guard FileManager.default.fileExists(atPath: fileURL.path) else {
      return nil
    }

    do {
      let data = try Data(contentsOf: fileURL)
      let snapshot = try decoder.decode(ActiveGenerationSnapshot.self, from: data)
      guard now.timeIntervalSince(snapshot.createdAt) <= expirationInterval else {
        try clear()
        return nil
      }

      return snapshot
    } catch {
      throw GifsterError.storageFailed(message: error.localizedDescription)
    }
  }

  public func save(_ snapshot: ActiveGenerationSnapshot) throws {
    do {
      try FileManager.default.createDirectory(
        at: fileURL.deletingLastPathComponent(),
        withIntermediateDirectories: true
      )
      let data = try encoder.encode(snapshot)
      try data.write(to: fileURL, options: .atomic)
    } catch {
      throw GifsterError.storageFailed(message: error.localizedDescription)
    }
  }

  public func clear() throws {
    do {
      guard FileManager.default.fileExists(atPath: fileURL.path) else {
        return
      }

      try FileManager.default.removeItem(at: fileURL)
    } catch {
      throw GifsterError.storageFailed(message: error.localizedDescription)
    }
  }
}
