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

public struct GIFRenderOptions: Sendable {
  public var maxPixelDimension: Int
  public var maxFrameCount: Int
  public var maxFileSizeBytes: Int?

  public init(
    maxPixelDimension: Int = 480,
    maxFrameCount: Int = 18,
    maxFileSizeBytes: Int? = nil
  ) {
    self.maxPixelDimension = maxPixelDimension
    self.maxFrameCount = maxFrameCount
    self.maxFileSizeBytes = maxFileSizeBytes
  }

  public static let messagesDefault = GIFRenderOptions(
    maxPixelDimension: 480,
    maxFrameCount: 18,
    maxFileSizeBytes: 4_000_000
  )
}

public struct GIFRenderer {
  public init() {}

  @discardableResult
  public func render(
    frames: [GIFFrame],
    to destinationURL: URL,
    loopCount: Int = 0,
    options: GIFRenderOptions = GIFRenderOptions()
  ) throws -> URL {
    let frames = try optimizedFrames(frames, options: options)
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

    if let maxFileSizeBytes = options.maxFileSizeBytes {
      let attributes = try FileManager.default.attributesOfItem(atPath: destinationURL.path)
      let fileSize = (attributes[.size] as? NSNumber)?.intValue ?? 0
      guard fileSize <= maxFileSizeBytes else {
        throw GifsterError.mediaRenderingFailed(message: "The GIF is too large for Messages. Try a shorter or simpler prompt.")
      }
    }

    return destinationURL
  }

  private func optimizedFrames(_ frames: [GIFFrame], options: GIFRenderOptions) throws -> [GIFFrame] {
    let limited = limitFrameCount(frames, maxFrameCount: options.maxFrameCount)
    return try limited.map { frame in
      GIFFrame(
        image: try downsample(frame.image, maxPixelDimension: options.maxPixelDimension),
        duration: frame.duration
      )
    }
  }

  private func limitFrameCount(_ frames: [GIFFrame], maxFrameCount: Int) -> [GIFFrame] {
    guard maxFrameCount > 0, frames.count > maxFrameCount else {
      return frames
    }

    let stride = Double(frames.count) / Double(maxFrameCount)
    return (0..<maxFrameCount).map { index in
      frames[min(Int(Double(index) * stride), frames.count - 1)]
    }
  }

  private func downsample(_ image: CGImage, maxPixelDimension: Int) throws -> CGImage {
    guard maxPixelDimension > 0 else {
      return image
    }

    let longestSide = max(image.width, image.height)
    guard longestSide > maxPixelDimension else {
      return image
    }

    let scale = Double(maxPixelDimension) / Double(longestSide)
    let width = max(1, Int(Double(image.width) * scale))
    let height = max(1, Int(Double(image.height) * scale))

    guard
      let colorSpace = image.colorSpace ?? CGColorSpace(name: CGColorSpace.sRGB),
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
      throw GifsterError.mediaRenderingFailed(message: "Could not downsample GIF frames.")
    }

    context.interpolationQuality = .high
    context.draw(image, in: CGRect(x: 0, y: 0, width: width, height: height))

    guard let result = context.makeImage() else {
      throw GifsterError.mediaRenderingFailed(message: "Could not create downsampled GIF frame.")
    }

    return result
  }
}
