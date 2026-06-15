import CoreGraphics
import Foundation
import ImageIO
import Testing
@testable import GifsterCore

@Suite("GIF pipeline")
struct GIFPipelineTests {
  @Test("Frame sequence renders a GIF file locally")
  func rendersGIFLocally() throws {
    let asset = DemoFrameSequence.make(prompt: "launch party")
    let frames = try FrameSequenceRenderer().renderFrames(from: asset, caption: "LAUNCH")
    let outputURL = FileManager.default.temporaryDirectory
      .appending(path: "gifster-test-\(UUID().uuidString).gif")

    try GIFRenderer().render(frames: frames, to: outputURL)

    let attributes = try FileManager.default.attributesOfItem(atPath: outputURL.path)
    let size = try #require(attributes[.size] as? NSNumber)
    #expect(size.intValue > 0)

    let data = try Data(contentsOf: outputURL)
    let source = CGImageSourceCreateWithData(data as CFData, nil)
    #expect(CGImageSourceGetCount(source!) == asset.frames.count)
  }
}
