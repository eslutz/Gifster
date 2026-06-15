import Foundation
import Messages
import Observation
import GifsterCore

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

private struct ActiveGenerationSnapshot: Codable {
  var job: GenerationJob
  var prompt: String
  var captionText: String?
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
  var recentItems: [GenerationHistoryItem] = []
  var presentationStyle: MSMessagesAppPresentationStyle = .compact

  @ObservationIgnored private let planner: any PromptPlanning = PromptPlannerFactory.makeDefaultPlanner()
  @ObservationIgnored private let defaults = UserDefaults(suiteName: AppStorageDirectories.appGroupIdentifier) ?? .standard
  @ObservationIgnored private let activeJobKey = "activeGifsterJob"
  @ObservationIgnored private let historyStore = GenerationHistoryStore(
    directoryURL: AppStorageDirectories.sharedContainerURL()
  )

  var canGenerate: Bool {
    !prompt.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty && phase != .planning && phase != .submitting && phase != .generating && phase != .rendering
  }

  var hasImage: Bool {
    sourceImageData != nil
  }

  func loadRecent() async {
    do {
      recentItems = try await historyStore.load()
    } catch {
      errorMessage = error.localizedDescription
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
        errorMessage = error.localizedDescription
      }
    }
  }

  func generate() {
    Task {
      do {
        errorMessage = nil
        previewGIFURL = nil
        phase = .planning

        var structuredRequest = try await planner.makeStructuredRequest(from: makeIntent(caption: captionRequestForGeneration()))
        if captionMode == .suggestWithAI && structuredRequest.caption.text?.isEmpty != false {
          captionSuggestions = try await planner.suggestCaptions(for: structuredRequest)
          selectedCaption = selectedCaption.isEmpty ? captionSuggestions.first?.text ?? "" : selectedCaption
          structuredRequest.caption = CaptionRequest(mode: .suggestWithAI, text: selectedCaption)
        }

        phase = .submitting
        let client = backendClient()
        let job = try await client.createJob(structuredRequest)
        persistActiveJob(ActiveGenerationSnapshot(
          job: job,
          prompt: structuredRequest.cleanedPrompt,
          captionText: structuredRequest.caption.text
        ))

        try await finish(job: job, prompt: structuredRequest.cleanedPrompt, captionText: structuredRequest.caption.text, client: client)
      } catch {
        phase = .idle
        errorMessage = error.localizedDescription
      }
    }
  }

  func resumeActiveJobIfNeeded() async {
    guard let snapshot = restoreActiveJob(), previewGIFURL == nil else {
      return
    }

    do {
      try await finish(
        job: snapshot.job,
        prompt: snapshot.prompt,
        captionText: snapshot.captionText,
        client: backendClient()
      )
    } catch {
      errorMessage = error.localizedDescription
    }
  }

  private func finish(
    job: GenerationJob,
    prompt: String,
    captionText: String?,
    client: GifsterBackendClient
  ) async throws {
    phase = .generating
    let completed = try await JobPollingService(client: client).waitForCompletion(startingWith: job)
    guard let downloadURL = completed.downloadURL else {
      throw GifsterError.jobFailed(message: "Backend completed without a result URL.")
    }

    let asset = try await client.downloadFrameSequence(from: downloadURL)
    phase = .rendering
    let frames = try FrameSequenceRenderer().renderFrames(from: asset, caption: captionText)
    let outputDirectory = try AppStorageDirectories.generatedMediaDirectory()
    let outputURL = outputDirectory.appending(path: "Gifster-\(UUID().uuidString).gif")
    try GIFRenderer().render(frames: frames, to: outputURL)

    previewGIFURL = outputURL
    phase = .preview
    clearActiveJob()
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

  private func backendClient() -> GifsterBackendClient {
    let rawValue = defaults.string(forKey: "backendBaseURL") ?? "http://127.0.0.1:8787"
    return GifsterBackendClient(baseURL: URL(string: rawValue) ?? URL(string: "http://127.0.0.1:8787")!)
  }

  private func persistActiveJob(_ snapshot: ActiveGenerationSnapshot) {
    if let data = try? JSONEncoder().encode(snapshot) {
      defaults.set(data, forKey: activeJobKey)
    }
  }

  private func restoreActiveJob() -> ActiveGenerationSnapshot? {
    guard let data = defaults.data(forKey: activeJobKey) else {
      return nil
    }
    return try? JSONDecoder().decode(ActiveGenerationSnapshot.self, from: data)
  }

  private func clearActiveJob() {
    defaults.removeObject(forKey: activeJobKey)
  }
}
