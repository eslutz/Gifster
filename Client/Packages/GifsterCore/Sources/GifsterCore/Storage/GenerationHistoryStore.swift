import Foundation

public actor GenerationHistoryStore {
  private let fileURL: URL
  private let encoder: JSONEncoder
  private let decoder: JSONDecoder

  public init(directoryURL: URL) {
    self.fileURL = directoryURL.appending(path: "generation-history.json")
    self.encoder = JSONEncoder()
    self.decoder = JSONDecoder()
    self.encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
    self.encoder.dateEncodingStrategy = .iso8601
    self.decoder.dateDecodingStrategy = .iso8601
  }

  public func load() throws -> [GenerationHistoryItem] {
    guard FileManager.default.fileExists(atPath: fileURL.path) else {
      return []
    }

    do {
      let data = try Data(contentsOf: fileURL)
      return try decoder.decode([GenerationHistoryItem].self, from: data)
    } catch {
      throw GifsterError.storageFailed(message: error.localizedDescription)
    }
  }

  public func save(_ item: GenerationHistoryItem) throws {
    var items = try load()
    items.removeAll { $0.id == item.id }
    items.insert(item, at: 0)
    items = Array(items.prefix(30))
    try write(items)
  }

  public func clear() throws {
    try write([])
  }

  private func write(_ items: [GenerationHistoryItem]) throws {
    do {
      try FileManager.default.createDirectory(
        at: fileURL.deletingLastPathComponent(),
        withIntermediateDirectories: true
      )
      let data = try encoder.encode(items)
      try data.write(to: fileURL, options: .atomic)
    } catch {
      throw GifsterError.storageFailed(message: error.localizedDescription)
    }
  }
}
