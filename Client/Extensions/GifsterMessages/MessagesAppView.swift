import PhotosUI
import GifsterCore
import SwiftUI
import UIKit
import WebKit

struct MessagesAppView: View {
  @Bindable var model: MessagesComposerModel
  var insertGIF: (URL) -> Void

  @State private var selectedPhotoItem: PhotosPickerItem?

  var body: some View {
    Group {
      if model.presentationStyle == .compact {
        compactBody
      } else {
        expandedBody
      }
    }
    .padding(12)
    .task {
      await model.loadRecent()
    }
    .alert("Gifster", isPresented: Binding(
      get: { model.errorMessage != nil },
      set: { if !$0 { model.errorMessage = nil } }
    )) {
      Button("OK", role: .cancel) {}
    } message: {
      Text(model.errorMessage ?? "")
    }
    .onChange(of: selectedPhotoItem) { _, newValue in
      Task {
        let data = try? await newValue?.loadTransferable(type: Data.self)
        model.setSourceImageData(data ?? nil)
      }
    }
  }

  private var compactBody: some View {
    VStack(spacing: 10) {
      HStack {
        TextField("Describe a GIF", text: $model.prompt)
          .textFieldStyle(.roundedBorder)
        photoButton
        generateButton
      }

      Picker("Caption", selection: $model.captionMode) {
        ForEach(CaptionMode.allCases) { mode in
          Text(mode.displayName).tag(mode)
        }
      }
      .pickerStyle(.segmented)

      statusRow

      if let previewGIFURL = model.previewGIFURL {
        GIFPreviewView(url: previewGIFURL)
          .frame(height: 126)
          .clipShape(RoundedRectangle(cornerRadius: 8))

        Button {
          insertGIF(previewGIFURL)
        } label: {
          Label("Insert", systemImage: "paperclip")
            .frame(maxWidth: .infinity)
        }
        .buttonStyle(.borderedProminent)
      } else {
        recentStrip
      }
    }
  }

  private var expandedBody: some View {
    VStack(alignment: .leading, spacing: 12) {
      HStack(alignment: .top, spacing: 12) {
        VStack(alignment: .leading, spacing: 10) {
          TextEditor(text: $model.prompt)
            .frame(minHeight: 88)
            .padding(8)
            .background(.thinMaterial, in: RoundedRectangle(cornerRadius: 8))

          HStack {
            photoButton
            Picker("Caption", selection: $model.captionMode) {
              ForEach(CaptionMode.allCases) { mode in
                Text(mode.displayName).tag(mode)
              }
            }
            .pickerStyle(.segmented)
          }

          captionControls
          generateButton
        }

        VStack(spacing: 10) {
          imagePreview
          statusRow
        }
        .frame(width: 160)
      }

      if let previewGIFURL = model.previewGIFURL {
        GIFPreviewView(url: previewGIFURL)
          .frame(maxHeight: 220)
          .clipShape(RoundedRectangle(cornerRadius: 8))

        HStack {
          Button {
            model.generate()
          } label: {
            Label("Regenerate", systemImage: "arrow.clockwise")
          }
          .buttonStyle(.bordered)

          Button {
            insertGIF(previewGIFURL)
          } label: {
            Label("Insert into Messages", systemImage: "paperclip")
              .frame(maxWidth: .infinity)
          }
          .buttonStyle(.borderedProminent)
        }
      } else {
        recentStrip
      }
    }
  }

  private var photoButton: some View {
    let hasImage = model.hasImage
    return PhotosPicker(selection: $selectedPhotoItem, matching: .images) {
      Label(hasImage ? "Image" : "Add", systemImage: hasImage ? "photo.fill" : "photo")
    }
    .buttonStyle(.bordered)
  }

  private var generateButton: some View {
    Button {
      model.generate()
    } label: {
      Label("Generate", systemImage: "sparkles")
    }
    .buttonStyle(.borderedProminent)
    .disabled(!model.canGenerate)
  }

  @ViewBuilder
  private var captionControls: some View {
    switch model.captionMode {
    case .none:
      EmptyView()
    case .userText:
      TextField("Caption", text: $model.explicitCaption)
        .textFieldStyle(.roundedBorder)
    case .suggestWithAI:
      VStack(alignment: .leading, spacing: 8) {
        HStack {
          TextField("Selected caption", text: $model.selectedCaption)
            .textFieldStyle(.roundedBorder)
          Button {
            model.requestCaptionSuggestions()
          } label: {
            Label("Suggest", systemImage: "wand.and.stars")
          }
          .buttonStyle(.bordered)
        }

        if !model.captionSuggestions.isEmpty {
          ScrollView(.horizontal, showsIndicators: false) {
            HStack {
              ForEach(model.captionSuggestions) { suggestion in
                Button(suggestion.text) {
                  model.selectedCaption = suggestion.text
                }
                .buttonStyle(.bordered)
              }
            }
          }
        }
      }
    }
  }

  private var imagePreview: some View {
    Group {
      if let data = model.sourceImageData, let image = UIImage(data: data) {
        Image(uiImage: image)
          .resizable()
          .scaledToFill()
      } else {
        ContentUnavailableView("No image", systemImage: "photo")
      }
    }
    .frame(width: 160, height: 120)
    .clipShape(RoundedRectangle(cornerRadius: 8))
  }

  private var statusRow: some View {
    HStack {
      if model.phase == .planning || model.phase == .submitting || model.phase == .generating || model.phase == .rendering {
        ProgressView()
          .controlSize(.small)
      }
      Text(model.phase.title)
        .font(.caption)
        .foregroundStyle(.secondary)
      Spacer()
    }
  }

  private var recentStrip: some View {
    ScrollView(.horizontal, showsIndicators: false) {
      HStack(spacing: 8) {
        ForEach(model.recentItems) { item in
          Button {
            model.previewGIFURL = item.gifURL
            model.phase = .preview
          } label: {
            VStack(alignment: .leading) {
              Image(systemName: "film")
                .font(.title2)
              Text(item.prompt)
                .font(.caption2)
                .lineLimit(2)
                .frame(width: 82, alignment: .leading)
            }
            .frame(width: 96, height: 76)
          }
          .buttonStyle(.bordered)
        }
      }
    }
  }
}

private struct GIFPreviewView: UIViewRepresentable {
  var url: URL

  func makeUIView(context: Context) -> WKWebView {
    let webView = WKWebView()
    webView.isOpaque = false
    webView.backgroundColor = .clear
    webView.scrollView.isScrollEnabled = false
    return webView
  }

  func updateUIView(_ webView: WKWebView, context: Context) {
    webView.loadFileURL(url, allowingReadAccessTo: url.deletingLastPathComponent())
  }
}
