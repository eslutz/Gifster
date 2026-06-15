import Messages
import GifsterCore
import SwiftUI
import UIKit

final class MessagesViewController: MSMessagesAppViewController {
  private let model = MessagesComposerModel()
  private var hostingController: UIHostingController<MessagesAppView>?

  override func viewDidLoad() {
    super.viewDidLoad()
    model.presentationStyle = presentationStyle
    installSwiftUIView()
  }

  override func didBecomeActive(with conversation: MSConversation) {
    super.didBecomeActive(with: conversation)
    Task {
      await model.loadRecent()
      await model.resumeActiveJobIfNeeded()
    }
  }

  override func willTransition(to presentationStyle: MSMessagesAppPresentationStyle) {
    super.willTransition(to: presentationStyle)
    model.presentationStyle = presentationStyle
  }

  private func installSwiftUIView() {
    let rootView = MessagesAppView(model: model) { [weak self, weak model] gifURL in
      self?.activeConversation?.insertAttachment(gifURL, withAlternateFilename: "Gifster.gif") { error in
        Task { @MainActor in
          if let error {
            model?.errorMessage = error.gifsterUserFacingMessage
          }
        }
      }
    }

    let controller = UIHostingController(rootView: rootView)
    addChild(controller)
    controller.view.translatesAutoresizingMaskIntoConstraints = false
    view.addSubview(controller.view)
    NSLayoutConstraint.activate([
      controller.view.leadingAnchor.constraint(equalTo: view.leadingAnchor),
      controller.view.trailingAnchor.constraint(equalTo: view.trailingAnchor),
      controller.view.topAnchor.constraint(equalTo: view.topAnchor),
      controller.view.bottomAnchor.constraint(equalTo: view.bottomAnchor)
    ])
    controller.didMove(toParent: self)
    hostingController = controller
  }
}
