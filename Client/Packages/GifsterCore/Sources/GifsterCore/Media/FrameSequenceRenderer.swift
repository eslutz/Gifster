import CoreGraphics
import Foundation

public struct FrameSequenceRenderer {
  public var captionRenderer: CaptionRenderer

  public init(captionRenderer: CaptionRenderer = CaptionRenderer()) {
    self.captionRenderer = captionRenderer
  }

  public func renderFrames(from asset: FrameSequenceAsset, caption: String?) throws -> [GIFFrame] {
    try asset.frames.map { frameSpec in
      let baseImage = try makeImage(frameSpec: frameSpec, width: asset.width, height: asset.height)
      let captioned = try captionRenderer.renderCaption(caption, over: baseImage)
      return GIFFrame(image: captioned, duration: frameSpec.duration)
    }
  }

  private func makeImage(frameSpec: FrameSpec, width: Int, height: Int) throws -> CGImage {
    guard
      let colorSpace = CGColorSpace(name: CGColorSpace.sRGB),
      let context = CGContext(
        data: nil,
        width: width,
        height: height,
        bitsPerComponent: 8,
        bytesPerRow: 0,
        space: colorSpace,
        bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue
      )
    else {
      throw GifsterError.mediaRenderingFailed(message: "Could not create frame drawing context.")
    }

    let canvas = CGRect(x: 0, y: 0, width: width, height: height)
    context.setFillColor(CGColor.promptGIFColor(hex: frameSpec.backgroundHex))
    context.fill(canvas)

    let accent = CGColor.promptGIFColor(hex: frameSpec.accentHex)
    context.setFillColor(accent.copy(alpha: 0.28) ?? accent)
    let offset = CGFloat(frameSpec.motionOffset)
    let bubbleSize = CGFloat(min(width, height)) * 0.42
    context.fillEllipse(in: CGRect(
      x: CGFloat(width) * 0.22 + offset,
      y: CGFloat(height) * 0.24,
      width: bubbleSize,
      height: bubbleSize
    ))

    context.setFillColor(accent)
    context.fillEllipse(in: CGRect(
      x: CGFloat(width) * 0.56 - offset * 0.4,
      y: CGFloat(height) * 0.42,
      width: bubbleSize * 0.62,
      height: bubbleSize * 0.62
    ))

    guard let image = context.makeImage() else {
      throw GifsterError.mediaRenderingFailed(message: "Could not create generated frame.")
    }

    return image
  }
}

private extension CGColor {
  static func promptGIFColor(hex: String) -> CGColor {
    let trimmed = hex.trimmingCharacters(in: CharacterSet(charactersIn: "#"))
    guard trimmed.count == 6, let value = Int(trimmed, radix: 16) else {
      return CGColor(red: 0.0, green: 0.52, blue: 0.58, alpha: 1.0)
    }

    let red = CGFloat((value >> 16) & 0xff) / 255
    let green = CGFloat((value >> 8) & 0xff) / 255
    let blue = CGFloat(value & 0xff) / 255
    return CGColor(red: red, green: green, blue: blue, alpha: 1.0)
  }
}
