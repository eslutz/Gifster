import Foundation

public enum GenerationMode: String, Codable, CaseIterable, Sendable {
  case textToGIF = "text_to_gif"
  case imageToGIF = "image_to_gif"
}

public enum MotionIntensity: String, Codable, CaseIterable, Sendable {
  case subtle
  case medium
  case high
}

public struct PromptStyleOptions: Codable, Equatable, Sendable {
  public var width: Int
  public var height: Int
  public var loopSeconds: Double
  public var stylePreset: String
  public var motionIntensity: MotionIntensity

  public init(
    width: Int = 480,
    height: Int = 360,
    loopSeconds: Double = 2.4,
    stylePreset: String = "expressive",
    motionIntensity: MotionIntensity = .medium
  ) {
    self.width = width
    self.height = height
    self.loopSeconds = loopSeconds
    self.stylePreset = stylePreset
    self.motionIntensity = motionIntensity
  }
}

public struct ProcessedSourceImage: Codable, Equatable, Sendable {
  public var mimeType: String
  public var width: Int
  public var height: Int
  public var dataBase64: String

  public init(mimeType: String, width: Int, height: Int, dataBase64: String) {
    self.mimeType = mimeType
    self.width = width
    self.height = height
    self.dataBase64 = dataBase64
  }
}

public struct SourceImageContext: Codable, Equatable, Sendable {
  public var width: Int
  public var height: Int
  public var orientation: String
  public var aspectRatio: String
  public var summary: String

  public init(
    width: Int,
    height: Int,
    orientation: String,
    aspectRatio: String,
    summary: String
  ) {
    self.width = width
    self.height = height
    self.orientation = orientation
    self.aspectRatio = aspectRatio
    self.summary = summary
  }

  public init(sourceImage: ProcessedSourceImage) {
    let orientation = Self.orientation(width: sourceImage.width, height: sourceImage.height)
    let aspectRatio = Self.aspectRatio(width: sourceImage.width, height: sourceImage.height)

    self.width = sourceImage.width
    self.height = sourceImage.height
    self.orientation = orientation
    self.aspectRatio = aspectRatio
    self.summary = "User-selected \(orientation) JPEG source image, \(sourceImage.width)x\(sourceImage.height), aspect \(aspectRatio)."
  }

  private static func orientation(width: Int, height: Int) -> String {
    if width == height {
      return "square"
    }

    return width > height ? "landscape" : "portrait"
  }

  private static func aspectRatio(width: Int, height: Int) -> String {
    guard width > 0, height > 0 else {
      return "unknown"
    }

    let divisor = greatestCommonDivisor(width, height)
    return "\(width / divisor):\(height / divisor)"
  }

  private static func greatestCommonDivisor(_ lhs: Int, _ rhs: Int) -> Int {
    var a = abs(lhs)
    var b = abs(rhs)

    while b != 0 {
      let remainder = a % b
      a = b
      b = remainder
    }

    return max(a, 1)
  }
}

public struct GenerationIntent: Codable, Equatable, Sendable {
  public var prompt: String
  public var sourceImage: ProcessedSourceImage?
  public var caption: CaptionRequest
  public var options: PromptStyleOptions

  public init(
    prompt: String,
    sourceImage: ProcessedSourceImage? = nil,
    caption: CaptionRequest = CaptionRequest(mode: .none),
    options: PromptStyleOptions = PromptStyleOptions()
  ) {
    self.prompt = prompt
    self.sourceImage = sourceImage
    self.caption = caption
    self.options = options
  }
}

public struct StructuredGenerationRequest: Codable, Equatable, Identifiable, Sendable {
  public var id: UUID
  public var mode: GenerationMode
  public var originalPrompt: String
  public var cleanedPrompt: String
  public var expandedPrompt: String
  public var negativePrompt: String
  public var caption: CaptionRequest
  public var sourceImage: ProcessedSourceImage?
  public var sourceImageContext: SourceImageContext?
  public var options: PromptStyleOptions
  public var clientTraceID: String

  public init(
    id: UUID = UUID(),
    mode: GenerationMode,
    originalPrompt: String,
    cleanedPrompt: String,
    expandedPrompt: String,
    negativePrompt: String,
    caption: CaptionRequest,
    sourceImage: ProcessedSourceImage?,
    sourceImageContext: SourceImageContext? = nil,
    options: PromptStyleOptions,
    clientTraceID: String = UUID().uuidString
  ) {
    self.id = id
    self.mode = mode
    self.originalPrompt = originalPrompt
    self.cleanedPrompt = cleanedPrompt
    self.expandedPrompt = expandedPrompt
    self.negativePrompt = negativePrompt
    self.caption = caption
    self.sourceImage = sourceImage
    self.sourceImageContext = sourceImageContext
    self.options = options
    self.clientTraceID = clientTraceID
  }
}

public enum GenerationStatus: String, Codable, Sendable {
  case queued
  case running
  case succeeded
  case failed
}

public struct GenerationJob: Codable, Equatable, Identifiable, Sendable {
  public var id: String
  public var status: GenerationStatus
  public var statusURL: URL
  public var downloadURL: URL?
  public var message: String?
  public var expiresAt: String?

  public var expirationDate: Date? {
    GifsterISO8601DateParser.date(from: expiresAt)
  }

  public init(
    id: String,
    status: GenerationStatus,
    statusURL: URL,
    downloadURL: URL? = nil,
    message: String? = nil,
    expiresAt: String? = nil
  ) {
    self.id = id
    self.status = status
    self.statusURL = statusURL
    self.downloadURL = downloadURL
    self.message = message
    self.expiresAt = expiresAt
  }
}

public struct JobSubmissionResponse: Codable, Equatable, Sendable {
  public var jobId: String
  public var status: GenerationStatus
  public var statusUrl: URL
  public var expiresAt: String

  public init(jobId: String, status: GenerationStatus, statusUrl: URL, expiresAt: String) {
    self.jobId = jobId
    self.status = status
    self.statusUrl = statusUrl
    self.expiresAt = expiresAt
  }
}

public struct JobStatusResponse: Codable, Equatable, Sendable {
  public var jobId: String
  public var status: GenerationStatus
  public var downloadUrl: URL?
  public var message: String?
  public var expiresAt: String

  public init(
    jobId: String,
    status: GenerationStatus,
    downloadUrl: URL? = nil,
    message: String? = nil,
    expiresAt: String
  ) {
    self.jobId = jobId
    self.status = status
    self.downloadUrl = downloadUrl
    self.message = message
    self.expiresAt = expiresAt
  }
}

public struct GenerationHistoryItem: Codable, Equatable, Identifiable, Sendable {
  public var id: UUID
  public var prompt: String
  public var captionText: String?
  public var gifURL: URL
  public var createdAt: Date

  public init(
    id: UUID = UUID(),
    prompt: String,
    captionText: String?,
    gifURL: URL,
    createdAt: Date = Date()
  ) {
    self.id = id
    self.prompt = prompt
    self.captionText = captionText
    self.gifURL = gifURL
    self.createdAt = createdAt
  }
}

public enum GifsterISO8601DateParser {
  public static func date(from value: String?) -> Date? {
    guard let value else {
      return nil
    }

    let fractionalFormatter = ISO8601DateFormatter()
    fractionalFormatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
    if let date = fractionalFormatter.date(from: value) {
      return date
    }

    let formatter = ISO8601DateFormatter()
    formatter.formatOptions = [.withInternetDateTime]
    return formatter.date(from: value)
  }
}
