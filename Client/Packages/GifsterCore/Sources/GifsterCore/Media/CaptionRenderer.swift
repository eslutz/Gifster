import CoreGraphics
import CoreText
import Foundation

public struct CaptionRenderingStyle: Sendable {
  public var fontSize: CGFloat
  public var bottomInset: CGFloat
  public var horizontalInset: CGFloat

  public init(fontSize: CGFloat = 32, bottomInset: CGFloat = 28, horizontalInset: CGFloat = 24) {
    self.fontSize = fontSize
    self.bottomInset = bottomInset
    self.horizontalInset = horizontalInset
  }
}

public struct CaptionRenderer {
  public var style: CaptionRenderingStyle

  public init(style: CaptionRenderingStyle = CaptionRenderingStyle()) {
    self.style = style
  }

  public func renderCaption(_ caption: String?, over image: CGImage) throws -> CGImage {
    guard let caption, !caption.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
      return image
    }

    guard
      let colorSpace = CGColorSpace(name: CGColorSpace.sRGB),
      let context = CGContext(
        data: nil,
        width: image.width,
        height: image.height,
        bitsPerComponent: 8,
        bytesPerRow: 0,
        space: colorSpace,
        bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue
      )
    else {
      throw GifsterError.mediaRenderingFailed(message: "Could not create a caption drawing context.")
    }

    let size = CGSize(width: image.width, height: image.height)
    context.draw(image, in: CGRect(origin: .zero, size: size))
    drawCaption(caption, in: context, canvasSize: size)

    guard let rendered = context.makeImage() else {
      throw GifsterError.mediaRenderingFailed(message: "Could not render captioned frame.")
    }

    return rendered
  }

  private func drawCaption(_ caption: String, in context: CGContext, canvasSize: CGSize) {
    context.saveGState()
    context.translateBy(x: 0, y: canvasSize.height)
    context.scaleBy(x: 1, y: -1)
    context.textMatrix = .identity

    let font = CTFontCreateWithName("HelveticaNeue-Bold" as CFString, style.fontSize, nil)
    let attributes = [
      kCTFontAttributeName: font,
      kCTForegroundColorAttributeName: CGColor(red: 1, green: 1, blue: 1, alpha: 1)
    ] as CFDictionary
    let attributed = CFAttributedStringCreate(nil, caption as CFString, attributes)
    let line = CTLineCreateWithAttributedString(attributed!)
    let bounds = CTLineGetBoundsWithOptions(line, [.useGlyphPathBounds, .useOpticalBounds])

    let x = max(style.horizontalInset, (canvasSize.width - bounds.width) / 2)
    let y = canvasSize.height - style.bottomInset - style.fontSize

    context.setShadow(
      offset: CGSize(width: 0, height: 2),
      blur: 6,
      color: CGColor(red: 0, green: 0, blue: 0, alpha: 0.75)
    )
    context.textPosition = CGPoint(x: x, y: y)
    CTLineDraw(line, context)
    context.restoreGState()
  }
}
