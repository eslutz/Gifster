import Foundation
import Messages
import Observation
import GifForgeCore

enum ComposerPhase: Equatable {
  case idle
  case planning
  case submitting
  case generating
  case rendering
  case preview

  var title: String {
    switch self {
    case .idle:
      "Ready"
    case .planning:
      "Planning"
    case .submitting:
      "Submitting"
    case .generating:
      "Generating"
    case .rendering:
      "Rendering"
    case .preview:
      "Preview"
    }
  }
}

@MainActor
@Observable
final class MessagesComposerModel {
  var prompt = ""
  var captionMode: CaptionMode = .none
  var explicitCaption = ""
  var selectedCaption = ""
  var captionSuggestions: [CaptionSuggestion] = []
  var sourceImageData: Data?
  var previewGIFURL: URL?
  var phase: ComposerPhase = .idle
  var errorMessage: String?
  var retryPromptMessage: String?
  var recentItems: [GenerationHistoryItem] = []
  var presentationStyle: MSMessagesAppPresentationStyle = .compact
  var canApplyCaptionEdit: Bool {
    phase == .preview && lastMotionAsset != nil
  }

  @ObservationIgnored private let planner: any PromptPlanning = PromptPlannerFactory.makeDefaultPlanner()
  @ObservationIgnored private let defaults = UserDefaults(suiteName: AppStorageDirectories.appGroupIdentifier) ?? .standard
  @ObservationIgnored private let activeGenerationStore = ActiveGenerationStore(
    directoryURL: AppStorageDirectories.sharedContainerURL()
  )
  @ObservationIgnored private let historyStore = GenerationHistoryStore(
    directoryURL: AppStorageDirectories.sharedContainerURL()
  )
  @ObservationIgnored private var lastMotionAsset: GeneratedMotionAsset?
  @ObservationIgnored private var lastPrompt: String?
  @ObservationIgnored private var pendingRetryMaterial: GenerationRetryMaterial?

  var canGenerate: Bool {
    !prompt.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty && phase != .planning && phase != .submitting && phase != .generating && phase != .rendering
  }

  var hasImage: Bool {
    sourceImageData != nil
  }

  var canRetryGeneration: Bool {
    retryPromptMessage != nil && phase == .idle
  }

  func loadRecent() async {
    do {
      recentItems = try await historyStore.load()
    } catch {
      errorMessage = error.gifforgeUserFacingMessage
    }
  }

  func setSourceImageData(_ data: Data?) {
    sourceImageData = data
  }

  func requestCaptionSuggestions() {
    Task {
      do {
        errorMessage = nil
        let request = try await planner.makeStructuredRequest(from: makeIntent(caption: CaptionRequest(mode: .suggestWithAI)))
        captionSuggestions = try await planner.suggestCaptions(for: request)
        selectedCaption = captionSuggestions.first?.text ?? ""
      } catch {
        errorMessage = error.gifforgeUserFacingMessage
      }
    }
  }

  func generate() {
    Task {
      do {
        errorMessage = nil
        retryPromptMessage = nil
        pendingRetryMaterial = nil
        previewGIFURL = nil
        lastMotionAsset = nil
        lastPrompt = nil
        phase = .planning

        var structuredRequest = try await planner.makeStructuredRequest(from: makeIntent(caption: captionRequestForGeneration()))
        if captionMode == .suggestWithAI && structuredRequest.caption.text?.isEmpty != false {
          captionSuggestions = try await planner.suggestCaptions(for: structuredRequest)
          selectedCaption = selectedCaption.isEmpty ? captionSuggestions.first?.text ?? "" : selectedCaption
          structuredRequest.caption = CaptionRequest(mode: .suggestWithAI, text: selectedCaption)
        }

        phase = .submitting
        let client = backendClient()
        let retryMaterial = GenerationRetryMaterial(request: structuredRequest)
        let job = try await client.createJob(structuredRequest)
        try await activeGenerationStore.save(ActiveGenerationSnapshot(
          job: job,
          prompt: structuredRequest.cleanedPrompt,
          captionText: structuredRequest.caption.text,
          retryMaterial: retryMaterial
        ))

        try await finish(
          job: job,
          prompt: structuredRequest.cleanedPrompt,
          captionText: structuredRequest.caption.text,
          retryMaterial: retryMaterial,
          client: client
        )
      } catch {
        phase = .idle
        errorMessage = error.gifforgeUserFacingMessage
      }
    }
  }

  func retryGeneration() {
    guard let retryMaterial = pendingRetryMaterial else {
      retryPromptMessage = nil
      return
    }

    Task {
      do {
        errorMessage = nil
        retryPromptMessage = nil
        pendingRetryMaterial = nil
        phase = .submitting

        let client = backendClient()
        let job = try await client.createJob(retryMaterial.request)
        try await activeGenerationStore.save(ActiveGenerationSnapshot(
          job: job,
          prompt: retryMaterial.request.cleanedPrompt,
          captionText: retryMaterial.request.caption.text,
          retryMaterial: retryMaterial
        ))

        try await finish(
          job: job,
          prompt: retryMaterial.request.cleanedPrompt,
          captionText: retryMaterial.request.caption.text,
          retryMaterial: retryMaterial,
          client: client
        )
      } catch {
        phase = .idle
        errorMessage = error.gifforgeUserFacingMessage
      }
    }
  }

  func dismissRetryPrompt() {
    retryPromptMessage = nil
  }

  func cancelPendingRetry() {
    pendingRetryMaterial = nil
    retryPromptMessage = nil
    Task {
      try? await activeGenerationStore.clear()
    }
  }

  func applyCaptionEdit() {
    Task {
      do {
        guard let lastMotionAsset else {
          return
        }

        errorMessage = nil
        let captionText = try captionTextForCurrentMode()
        try await renderPreview(
          from: lastMotionAsset,
          prompt: lastPrompt ?? prompt,
          captionText: captionText
        )
      } catch {
        phase = .preview
        errorMessage = error.gifforgeUserFacingMessage
      }
    }
  }

  func resumeActiveJobIfNeeded() async {
    do {
      guard let snapshot = try await activeGenerationStore.load(), previewGIFURL == nil else {
        return
      }

      try await finish(
        job: snapshot.job,
        prompt: snapshot.prompt,
        captionText: snapshot.captionText,
        retryMaterial: snapshot.retryMaterial,
        client: backendClient()
      )
    } catch {
      errorMessage = error.gifforgeUserFacingMessage
    }
  }

  private func finish(
    job: GenerationJob,
    prompt: String,
    captionText: String?,
    retryMaterial: GenerationRetryMaterial?,
    client: GifForgeBackendClient
  ) async throws {
    phase = .generating
    let completed: GenerationJob
    do {
      completed = try await JobPollingService(client: client).waitForCompletion(startingWith: job)
    } catch let GifForgeError.retryAvailable(failedJob, message) {
      try await prepareClientRetry(
        failedJob: failedJob,
        message: message,
        prompt: prompt,
        captionText: captionText,
        retryMaterial: retryMaterial
      )
      return
    }

    guard let downloadURL = completed.downloadURL else {
      throw GifForgeError.jobFailed(message: "Backend completed without a result URL.")
    }

    let asset = try await client.downloadMotionAsset(from: downloadURL)
    lastMotionAsset = asset
    lastPrompt = prompt

    try await renderPreview(from: asset, prompt: prompt, captionText: captionText)
    try await activeGenerationStore.clear()
  }

  private func prepareClientRetry(
    failedJob: GenerationJob,
    message: String,
    prompt: String,
    captionText: String?,
    retryMaterial: GenerationRetryMaterial?
  ) async throws {
    guard let retryMaterial else {
      throw GifForgeError.jobFailed(message: message)
    }

    var retryRequest = retryMaterial.request
    retryRequest.retryOfJobId = failedJob.retryOfJobId ?? failedJob.id
    let updatedMaterial = GenerationRetryMaterial(request: retryRequest)
    pendingRetryMaterial = updatedMaterial
    retryPromptMessage = message
    phase = .idle

    try await activeGenerationStore.save(ActiveGenerationSnapshot(
      job: failedJob,
      prompt: prompt,
      captionText: captionText,
      retryMaterial: updatedMaterial
    ))
  }

  private func renderPreview(
    from asset: GeneratedMotionAsset,
    prompt: String,
    captionText: String?
  ) async throws {
    phase = .rendering
    let frames = try await MotionAssetFrameRenderer().renderFrames(from: asset, caption: captionText)
    let outputDirectory = try AppStorageDirectories.generatedMediaDirectory()
    let outputURL = outputDirectory.appending(path: "GifForge-\(UUID().uuidString).gif")
    try GIFRenderer().render(frames: frames, to: outputURL, options: .messagesDefault)

    previewGIFURL = outputURL
    phase = .preview
    try await historyStore.save(GenerationHistoryItem(prompt: prompt, captionText: captionText, gifURL: outputURL))
    await loadRecent()
  }

  private func makeIntent(caption: CaptionRequest) throws -> GenerationIntent {
    let sourceImage = try sourceImageData.map { try ImagePreprocessor().processImageData($0) }
    return GenerationIntent(prompt: prompt, sourceImage: sourceImage, caption: caption)
  }

  private func captionRequestForGeneration() throws -> CaptionRequest {
    switch captionMode {
    case .none:
      CaptionRequest(mode: .none)
    case .userText:
      CaptionRequest(mode: .userText, text: explicitCaption)
    case .suggestWithAI:
      CaptionRequest(mode: .suggestWithAI, text: selectedCaption)
    }
  }

  private func captionTextForCurrentMode() throws -> String? {
    switch captionMode {
    case .none:
      nil
    case .userText:
      try CaptionValidator.normalizedExplicitCaption(explicitCaption)
    case .suggestWithAI:
      try CaptionValidator.normalizedExplicitCaption(selectedCaption)
    }
  }

  private func backendClient() -> GifForgeBackendClient {
    let rawValue = defaults.string(forKey: "backendBaseURL") ?? "http://127.0.0.1:8787"
    let baseURL = URL(string: rawValue) ?? URL(string: "http://127.0.0.1:8787")!
    let tokenAuthorizer = StoredBearerTokenAuthorizer(provider: KeychainBackendAuthTokenStore())

    guard defaults.bool(forKey: "backendRequiresAppAttest") else {
      return GifForgeBackendClient(baseURL: baseURL, authorizer: tokenAuthorizer)
    }

    #if os(iOS)
    let bootstrapClient = GifForgeBackendClient(baseURL: baseURL)
    let provider = DeviceCheckAppAttestSessionProvider(backendClient: bootstrapClient)
    return GifForgeBackendClient(
      baseURL: baseURL,
      authorizer: CompositeBackendRequestAuthorizer(authorizers: [
        tokenAuthorizer,
        AppAttestSessionAuthorizer(provider: provider)
      ])
    )
    #else
    return GifForgeBackendClient(baseURL: baseURL, authorizer: tokenAuthorizer)
    #endif
  }

}
