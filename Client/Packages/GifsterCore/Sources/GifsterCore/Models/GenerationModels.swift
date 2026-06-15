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

  public init(
    id: String,
    status: GenerationStatus,
    statusURL: URL,
    downloadURL: URL? = nil,
    message: String? = nil
  ) {
    self.id = id
    self.status = status
    self.statusURL = statusURL
    self.downloadURL = downloadURL
    self.message = message
  }
}

public struct JobSubmissionResponse: Codable, Equatable, Sendable {
  public var jobId: String
  public var status: GenerationStatus
  public var statusUrl: URL

  public init(jobId: String, status: GenerationStatus, statusUrl: URL) {
    self.jobId = jobId
    self.status = status
    self.statusUrl = statusUrl
  }
}

public struct JobStatusResponse: Codable, Equatable, Sendable {
  public var jobId: String
  public var status: GenerationStatus
  public var downloadUrl: URL?
  public var message: String?

  public init(
    jobId: String,
    status: GenerationStatus,
    downloadUrl: URL? = nil,
    message: String? = nil
  ) {
    self.jobId = jobId
    self.status = status
    self.downloadUrl = downloadUrl
    self.message = message
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
