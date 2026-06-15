import CoreGraphics
import Foundation
import ImageIO
import UniformTypeIdentifiers

public struct GIFFrame {
  public var image: CGImage
  public var duration: Double

  public init(image: CGImage, duration: Double) {
    self.image = image
    self.duration = duration
  }
}

public struct GIFRenderer {
  public init() {}

  @discardableResult
  public func render(
    frames: [GIFFrame],
    to destinationURL: URL,
    loopCount: Int = 0
  ) throws -> URL {
    guard !frames.isEmpty else {
      throw GifsterError.mediaRenderingFailed(message: "Cannot render a GIF without frames.")
    }

    guard let destination = CGImageDestinationCreateWithURL(
      destinationURL as CFURL,
      UTType.gif.identifier as CFString,
      frames.count,
      nil
    ) else {
      throw GifsterError.mediaRenderingFailed(message: "Could not create GIF destination.")
    }

    let fileProperties = [
      kCGImagePropertyGIFDictionary as String: [
        kCGImagePropertyGIFLoopCount as String: loopCount
      ]
    ] as CFDictionary
    CGImageDestinationSetProperties(destination, fileProperties)

    for frame in frames {
      let frameProperties = [
        kCGImagePropertyGIFDictionary as String: [
          kCGImagePropertyGIFDelayTime as String: max(frame.duration, 0.02)
        ]
      ] as CFDictionary
      CGImageDestinationAddImage(destination, frame.image, frameProperties)
    }

    guard CGImageDestinationFinalize(destination) else {
      throw GifsterError.mediaRenderingFailed(message: "ImageIO could not finalize the GIF.")
    }

    return destinationURL
  }
}
