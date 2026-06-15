import CoreGraphics
import Foundation
import ImageIO
import UniformTypeIdentifiers

public struct ImagePreprocessor {
  public var maxPixelDimension: Int
  public var compressionQuality: Double

  public init(maxPixelDimension: Int = 1024, compressionQuality: Double = 0.82) {
    self.maxPixelDimension = maxPixelDimension
    self.compressionQuality = compressionQuality
  }

  public func processImageData(_ data: Data) throws -> ProcessedSourceImage {
    guard
      let source = CGImageSourceCreateWithData(data as CFData, nil),
      let image = CGImageSourceCreateImageAtIndex(source, 0, nil)
    else {
      throw GifsterError.invalidImage
    }

    let scaledImage = try downscaledImage(image)
    let jpegData = try jpegData(from: scaledImage)

    return ProcessedSourceImage(
      mimeType: "image/jpeg",
      width: scaledImage.width,
      height: scaledImage.height,
      dataBase64: jpegData.base64EncodedString()
    )
  }

  private func downscaledImage(_ image: CGImage) throws -> CGImage {
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
      throw GifsterError.invalidImage
    }

    context.interpolationQuality = .high
    context.draw(image, in: CGRect(x: 0, y: 0, width: width, height: height))

    guard let result = context.makeImage() else {
      throw GifsterError.invalidImage
    }

    return result
  }

  private func jpegData(from image: CGImage) throws -> Data {
    let output = NSMutableData()
    guard let destination = CGImageDestinationCreateWithData(
      output,
      UTType.jpeg.identifier as CFString,
      1,
      nil
    ) else {
      throw GifsterError.invalidImage
    }

    let properties = [
      kCGImageDestinationLossyCompressionQuality as String: compressionQuality
    ] as CFDictionary
    CGImageDestinationAddImage(destination, image, properties)

    guard CGImageDestinationFinalize(destination) else {
      throw GifsterError.invalidImage
    }

    return output as Data
  }
}
