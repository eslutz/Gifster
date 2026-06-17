import GifForgeCore
import AuthenticationServices
import CryptoKit
import Security
import SwiftUI
#if canImport(StoreKit)
import StoreKit
#endif

@MainActor
private let gifforgeDefaults = UserDefaults(suiteName: AppStorageDirectories.appGroupIdentifier) ?? .standard

struct AppShellView: View {
  @State private var history: [GenerationHistoryItem] = []
  @State private var errorMessage: String?

  private let historyStore = GenerationHistoryStore(
    directoryURL: AppStorageDirectories.sharedContainerURL()
  )
  private let activeGenerationStore = ActiveGenerationStore(
    directoryURL: AppStorageDirectories.sharedContainerURL()
  )

  var body: some View {
    TabView {
      NavigationStack {
        OverviewView()
      }
      .tabItem {
        Label("GifForge", systemImage: "sparkles")
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
    .alert("GifForge", isPresented: Binding(
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
      #if DEBUG
      try await seedHistoryForUITestsIfNeeded()
      #endif
      history = try await historyStore.load()
    } catch {
      errorMessage = error.gifforgeUserFacingMessage
    }
  }

  private func clearHistory() {
    Task {
      do {
        try await historyStore.clear()
        try await activeGenerationStore.clear()
        history = []
      } catch {
        errorMessage = error.gifforgeUserFacingMessage
      }
    }
  }

  #if DEBUG
  private func seedHistoryForUITestsIfNeeded() async throws {
    guard ProcessInfo.processInfo.environment["GIFFORGE_UI_TEST_SEED_HISTORY"] == "1" else {
      return
    }

    let existingHistory = try await historyStore.load()
    guard existingHistory.isEmpty else {
      return
    }

    let mediaDirectory = try AppStorageDirectories.generatedMediaDirectory()
    let gifURL = mediaDirectory.appending(path: "ui-test-history.gif")
    if !FileManager.default.fileExists(atPath: gifURL.path) {
      try Data("GIF89a".utf8).write(to: gifURL, options: .atomic)
    }

    try await historyStore.save(
      GenerationHistoryItem(
        prompt: "UI test prompt",
        captionText: "UI test caption",
        gifURL: gifURL
      )
    )
  }
  #endif
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
        Text("Prompts and selected images are sent through your GifForge backend for media generation. Prompt cleanup and caption suggestions run locally when Apple Foundation Models are available.")
        Text("Generated GIFs and resumable job metadata are stored locally only as needed and can be cleared from this app.")
      }

      Section("Messages") {
        Text("Open GifForge from the Messages app drawer, generate a GIF, preview it, then insert it into the compose field. Messages always requires you to send manually.")
      }
    }
    .navigationTitle("GifForge")
  }
}

private struct HistoryView: View {
  var history: [GenerationHistoryItem]
  var clearHistory: () -> Void

  @State private var isConfirmingClearHistory = false

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
      Button(role: .destructive) {
        isConfirmingClearHistory = true
      } label: {
        Label("Clear", systemImage: "trash")
      }
      .disabled(history.isEmpty)
    }
    .alert("Clear History?", isPresented: $isConfirmingClearHistory) {
      Button("Cancel", role: .cancel) {}
      Button("Clear History", role: .destructive, action: clearHistory)
    } message: {
      Text("Delete generated GIF history and resumable job metadata from this device.")
    }
  }
}

private struct SettingsView: View {
  @AppStorage("backendBaseURL", store: gifforgeDefaults)
  private var backendBaseURL = "http://127.0.0.1:8787"
  @AppStorage("backendRequiresAppAttest", store: gifforgeDefaults)
  private var backendRequiresAppAttest = false
  @State private var userID: String?
  @State private var appAccountToken: UUID?
  @State private var creditBalance: BackendCreditBalance?
  @State private var settingsMessage: String?
  @State private var isRefreshingAccount = false
  @State private var currentAppleSignInNonce: String?
  #if canImport(StoreKit)
  @State private var storeProducts: [Product] = []
  #endif

  private let tokenStore = KeychainBackendAuthTokenStore()

  var body: some View {
    Form {
      Section("Account") {
        if let userID {
          LabeledContent("User", value: userID)
            .font(.caption)
          if let creditBalance {
            LabeledContent("Available Credits", value: "\(creditBalance.availableCredits)")
            LabeledContent("Reserved", value: "\(creditBalance.reservedCredits)")
          }
          Button {
            refreshAccount()
          } label: {
            Label("Refresh", systemImage: "arrow.clockwise")
          }
          .disabled(isRefreshingAccount)
          Button(role: .destructive) {
            tokenStore.clear()
            self.userID = nil
            appAccountToken = nil
            creditBalance = nil
            #if canImport(StoreKit)
            storeProducts = []
            #endif
          } label: {
            Label("Sign Out", systemImage: "rectangle.portrait.and.arrow.right")
          }
        } else {
          SignInWithAppleButton(.signIn) { request in
            request.requestedScopes = [.email]
            let nonce = Self.randomNonceString()
            currentAppleSignInNonce = nonce
            request.nonce = Self.sha256(nonce)
          } onCompletion: { result in
            handleSignIn(result)
          }
          .frame(height: 44)
        }

        if let settingsMessage {
          Text(settingsMessage)
            .font(.caption)
            .foregroundStyle(.secondary)
        }
      }

      #if canImport(StoreKit)
      if appAccountToken != nil {
        Section("Credits") {
          if storeProducts.isEmpty {
            Button {
              refreshAccount()
            } label: {
              Label("Load Credit Packs", systemImage: "cart")
            }
          } else {
            ForEach(storeProducts, id: \.id) { product in
              Button {
                purchase(product)
              } label: {
                HStack {
                  Text(product.displayName)
                  Spacer()
                  Text(product.displayPrice)
                    .foregroundStyle(.secondary)
                }
              }
            }
          }
        }
      }
      #endif

      Section("Backend") {
        TextField("Base URL", text: $backendBaseURL)
          .textInputAutocapitalization(.never)
          .keyboardType(.URL)
        Toggle("Require App Attest", isOn: $backendRequiresAppAttest)
      }

      Section("Development") {
        Text("Run the local backend with dotnet run from the Backend project. Use your Mac LAN IP for device testing.")
      }
    }
    .navigationTitle("Settings")
    .task {
      await restoreAccount()
    }
  }

  private var baseURL: URL {
    URL(string: backendBaseURL) ?? URL(string: "http://127.0.0.1:8787")!
  }

  private func unauthenticatedClient() -> GifForgeBackendClient {
    GifForgeBackendClient(baseURL: baseURL)
  }

  private func authenticatedClient() -> GifForgeBackendClient {
    GifForgeBackendClient(
      baseURL: baseURL,
      authorizer: StoredBearerTokenAuthorizer(provider: tokenStore)
    )
  }

  private func handleSignIn(_ result: Result<ASAuthorization, Error>) {
    Task {
      do {
        guard case let .success(authorization) = result,
              let credential = authorization.credential as? ASAuthorizationAppleIDCredential,
              let tokenData = credential.identityToken,
              let identityToken = String(data: tokenData, encoding: .utf8)
        else {
          settingsMessage = "Sign in was cancelled."
          return
        }

        let session = try await unauthenticatedClient().signInWithApple(
          identityToken: identityToken,
          nonce: currentAppleSignInNonce
        )
        currentAppleSignInNonce = nil
        try tokenStore.save(session: session)
        userID = session.userID
        appAccountToken = session.appAccountToken
        await loadCreditsAndProducts()
      } catch {
        settingsMessage = error.gifforgeUserFacingMessage
      }
    }
  }

  private func refreshAccount() {
    Task {
      await loadCreditsAndProducts()
    }
  }

  private func restoreAccount() async {
    do {
      guard try tokenStore.load() != nil else {
        return
      }
      let profile = try await authenticatedClient().fetchMe()
      userID = profile.userID
      appAccountToken = profile.appAccountToken
      await loadCreditsAndProducts()
    } catch {
      settingsMessage = error.gifforgeUserFacingMessage
    }
  }

  private func loadCreditsAndProducts() async {
    isRefreshingAccount = true
    defer { isRefreshingAccount = false }
    do {
      creditBalance = try await authenticatedClient().fetchCreditBalance()
      #if canImport(StoreKit)
      if let appAccountToken {
        let service = StoreKitCreditPurchaseService(
          backendClient: authenticatedClient(),
          appAccountToken: appAccountToken
        )
        storeProducts = try await service.products()
      }
      #endif
      settingsMessage = nil
    } catch {
      settingsMessage = error.gifforgeUserFacingMessage
    }
  }

  #if canImport(StoreKit)
  private func purchase(_ product: Product) {
    Task {
      guard let appAccountToken else {
        settingsMessage = "Sign in before buying credits."
        return
      }
      do {
        let service = StoreKitCreditPurchaseService(
          backendClient: authenticatedClient(),
          appAccountToken: appAccountToken
        )
        _ = try await service.purchase(product)
        await loadCreditsAndProducts()
      } catch {
        settingsMessage = error.gifforgeUserFacingMessage
      }
    }
  }
  #endif

  private static func randomNonceString(length: Int = 32) -> String {
    precondition(length > 0)
    let charset = Array("0123456789ABCDEFGHIJKLMNOPQRSTUVXYZabcdefghijklmnopqrstuvwxyz-._")
    var result = ""
    var randomBytes = [UInt8](repeating: 0, count: length)
    let status = SecRandomCopyBytes(kSecRandomDefault, randomBytes.count, &randomBytes)
    guard status == errSecSuccess else {
      return UUID().uuidString.replacingOccurrences(of: "-", with: "")
    }

    for byte in randomBytes {
      result.append(charset[Int(byte) % charset.count])
    }
    return result
  }

  private static func sha256(_ input: String) -> String {
    let inputData = Data(input.utf8)
    let hashedData = SHA256.hash(data: inputData)
    return hashedData.map { String(format: "%02x", $0) }.joined()
  }
}
