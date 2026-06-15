import AVFoundation
import CoreGraphics
import Foundation

public struct MP4FrameExtractor: Sendable {
  public var frameCount: Int
  public var maximumPixelDimension: Int
  public var captionRenderer: CaptionRenderer

  public init(
    frameCount: Int = 18,
    maximumPixelDimension: Int = 480,
    captionRenderer: CaptionRenderer = CaptionRenderer()
  ) {
    self.frameCount = frameCount
    self.maximumPixelDimension = maximumPixelDimension
    self.captionRenderer = captionRenderer
  }

  public func extractFrames(from url: URL, caption: String?) async throws -> [GIFFrame] {
    let asset = AVURLAsset(url: url)
    let duration = try await asset.load(.duration)
    guard duration.seconds.isFinite, duration.seconds > 0 else {
      throw GifsterError.mediaRenderingFailed(message: "The MP4 result has no readable duration.")
    }

    let actualFrameCount = max(1, frameCount)
    let generator = AVAssetImageGenerator(asset: asset)
    generator.appliesPreferredTrackTransform = true
    generator.maximumSize = CGSize(
      width: maximumPixelDimension,
      height: maximumPixelDimension
    )

    let step = duration.seconds / Double(actualFrameCount)
    let frameDuration = max(step, 0.02)
    var frames: [GIFFrame] = []

    for index in 0..<actualFrameCount {
      let seconds = min(Double(index) * step, max(duration.seconds - 0.001, 0))
      let time = CMTime(seconds: seconds, preferredTimescale: 600)
      let image = try await generator.image(at: time).image
      let captioned = try captionRenderer.renderCaption(caption, over: image)
      frames.append(GIFFrame(image: captioned, duration: frameDuration))
    }

    return frames
  }
}
