import Foundation

public enum AppStorageDirectories {
  public static let appGroupIdentifier = "group.dev.ericslutz.gifforge"
  public static let keychainAccessGroup = "QS3GC3CT43.dev.ericslutz.gifforge.auth"

  public static func sharedContainerURL(fileManager: FileManager = .default) -> URL {
    if let containerURL = fileManager.containerURL(forSecurityApplicationGroupIdentifier: appGroupIdentifier) {
      return containerURL
    }

    return fileManager.urls(for: .documentDirectory, in: .userDomainMask)[0]
  }

  public static func generatedMediaDirectory(fileManager: FileManager = .default) throws -> URL {
    let url = sharedContainerURL(fileManager: fileManager).appending(path: "GeneratedGIFs", directoryHint: .isDirectory)
    try fileManager.createDirectory(at: url, withIntermediateDirectories: true)
    return url
  }
}
