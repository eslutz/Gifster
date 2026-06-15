import GifsterCore
import SwiftUI

@MainActor
private let gifsterDefaults = UserDefaults(suiteName: AppStorageDirectories.appGroupIdentifier) ?? .standard

struct AppShellView: View {
  @State private var history: [GenerationHistoryItem] = []
  @State private var errorMessage: String?

  private let historyStore = GenerationHistoryStore(
    directoryURL: AppStorageDirectories.sharedContainerURL()
  )

  var body: some View {
    TabView {
      NavigationStack {
        OverviewView()
      }
      .tabItem {
        Label("Gifster", systemImage: "sparkles")
      }

      NavigationStack {
        HistoryView(history: history, clearHistory: clearHistory)
          .task {
            await loadHistory()
          }
      }
      .tabItem {
        Label("History", systemImage: "clock")
      }

      NavigationStack {
        SettingsView()
      }
      .tabItem {
        Label("Settings", systemImage: "gearshape")
      }
    }
    .alert("Gifster", isPresented: Binding(
      get: { errorMessage != nil },
      set: { if !$0 { errorMessage = nil } }
    )) {
      Button("OK", role: .cancel) {}
    } message: {
      Text(errorMessage ?? "")
    }
  }

  private func loadHistory() async {
    do {
      history = try await historyStore.load()
    } catch {
      errorMessage = error.localizedDescription
    }
  }

  private func clearHistory() {
    Task {
      do {
        try await historyStore.clear()
        history = []
      } catch {
        errorMessage = error.localizedDescription
      }
    }
  }
}

private struct OverviewView: View {
  var body: some View {
    List {
      Section {
        Label("Generate GIFs from text prompts inside Messages.", systemImage: "message")
        Label("Animate a selected image without broad photo library access.", systemImage: "photo")
        Label("Review captions locally before inserting the GIF.", systemImage: "text.bubble")
      }

      Section("Privacy") {
        Text("Prompts and selected images are sent through your Gifster backend for media generation. Prompt cleanup and caption suggestions run locally when Apple Foundation Models are available.")
        Text("Generated GIFs are stored locally for recent history and can be cleared from this app.")
      }

      Section("Messages") {
        Text("Open Gifster from the Messages app drawer, generate a GIF, preview it, then insert it into the compose field. Messages always requires you to send manually.")
      }
    }
    .navigationTitle("Gifster")
  }
}

private struct HistoryView: View {
  var history: [GenerationHistoryItem]
  var clearHistory: () -> Void

  var body: some View {
    List {
      if history.isEmpty {
        ContentUnavailableView("No GIFs yet", systemImage: "sparkles")
      } else {
        ForEach(history) { item in
          VStack(alignment: .leading, spacing: 6) {
            Text(item.prompt)
              .font(.headline)
              .lineLimit(2)
            if let caption = item.captionText, !caption.isEmpty {
              Text(caption)
                .font(.subheadline)
                .foregroundStyle(.secondary)
            }
            ShareLink(item: item.gifURL) {
              Label("Share GIF", systemImage: "square.and.arrow.up")
            }
            .font(.caption)
          }
          .padding(.vertical, 4)
        }
      }
    }
    .navigationTitle("History")
    .toolbar {
      Button(role: .destructive, action: clearHistory) {
        Label("Clear", systemImage: "trash")
      }
      .disabled(history.isEmpty)
    }
  }
}

private struct SettingsView: View {
  @AppStorage("backendBaseURL", store: gifsterDefaults)
  private var backendBaseURL = "http://127.0.0.1:8787"

  var body: some View {
    Form {
      Section("Backend") {
        TextField("Base URL", text: $backendBaseURL)
          .textInputAutocapitalization(.never)
          .keyboardType(.URL)
      }

      Section("Development") {
        Text("Run the local backend with dotnet run from the Backend project. Use your Mac LAN IP for device testing.")
      }
    }
    .navigationTitle("Settings")
  }
}
