import CoreGraphics
import CoreText
import Foundation

public struct CaptionRenderingStyle: Sendable {
  public var fontSize: CGFloat
  public var minimumFontSize: CGFloat
  public var bottomInset: CGFloat
  public var horizontalInset: CGFloat
  public var maxLines: Int

  public init(
    fontSize: CGFloat = 32,
    minimumFontSize: CGFloat = 18,
    bottomInset: CGFloat = 28,
    horizontalInset: CGFloat = 24,
    maxLines: Int = 2
  ) {
    self.fontSize = fontSize
    self.minimumFontSize = minimumFontSize
    self.bottomInset = bottomInset
    self.horizontalInset = horizontalInset
    self.maxLines = maxLines
  }
}

public struct CaptionTextLayout: Sendable {
  public var lines: [String]
  public var fontSize: CGFloat
  public var textBounds: CGRect

  public init(lines: [String], fontSize: CGFloat, textBounds: CGRect) {
    self.lines = lines
    self.fontSize = fontSize
    self.textBounds = textBounds
  }
}

public struct CaptionRenderer: Sendable {
  public var style: CaptionRenderingStyle

  public init(style: CaptionRenderingStyle = CaptionRenderingStyle()) {
    self.style = style
  }

  public func layoutCaption(_ caption: String, canvasSize: CGSize) -> CaptionTextLayout {
    let maxWidth = max(1, canvasSize.width - style.horizontalInset * 2)
    let maxLines = max(1, style.maxLines)
    let words = caption
      .replacingOccurrences(of: "\n", with: " ")
      .split(separator: " ")
      .map(String.init)

    var fontSize = style.fontSize
    while fontSize >= style.minimumFontSize {
      let lines = wrappedLines(words: words, fontSize: fontSize, maxWidth: maxWidth, maxLines: maxLines)
      let widest = lines.map { lineWidth($0, fontSize: fontSize) }.max() ?? 0
      if lines.count <= maxLines && widest <= maxWidth {
        return CaptionTextLayout(
          lines: lines,
          fontSize: fontSize,
          textBounds: CGRect(
            x: 0,
            y: 0,
            width: widest,
            height: CGFloat(lines.count) * fontSize * 1.16
          )
        )
      }

      fontSize -= 1
    }

    let lines = wrappedLines(
      words: words,
      fontSize: style.minimumFontSize,
      maxWidth: maxWidth,
      maxLines: maxLines
    )
    return CaptionTextLayout(
      lines: lines,
      fontSize: style.minimumFontSize,
      textBounds: CGRect(
        x: 0,
        y: 0,
        width: min(maxWidth, lines.map { lineWidth($0, fontSize: style.minimumFontSize) }.max() ?? 0),
        height: CGFloat(lines.count) * style.minimumFontSize * 1.16
      )
    )
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

    let layout = layoutCaption(caption, canvasSize: canvasSize)
    let font = CTFontCreateWithName("HelveticaNeue-Bold" as CFString, layout.fontSize, nil)
    let lineHeight = layout.fontSize * 1.16
    var y = canvasSize.height - style.bottomInset - layout.textBounds.height
    context.setShadow(
      offset: CGSize(width: 0, height: 2),
      blur: 6,
      color: CGColor(red: 0, green: 0, blue: 0, alpha: 0.75)
    )

    for text in layout.lines {
      let attributes = [
        kCTFontAttributeName: font,
        kCTForegroundColorAttributeName: CGColor(red: 1, green: 1, blue: 1, alpha: 1)
      ] as CFDictionary
      let attributed = CFAttributedStringCreate(nil, text as CFString, attributes)
      let line = CTLineCreateWithAttributedString(attributed!)
      let bounds = CTLineGetBoundsWithOptions(line, [.useGlyphPathBounds, .useOpticalBounds])
      let x = max(style.horizontalInset, (canvasSize.width - bounds.width) / 2)
      context.textPosition = CGPoint(x: x, y: y)
      CTLineDraw(line, context)
      y += lineHeight
    }

    context.restoreGState()
  }

  private func wrappedLines(
    words: [String],
    fontSize: CGFloat,
    maxWidth: CGFloat,
    maxLines: Int
  ) -> [String] {
    guard !words.isEmpty else {
      return []
    }

    var lines: [String] = []
    var current = ""

    for word in words {
      let candidate = current.isEmpty ? word : "\(current) \(word)"
      if lineWidth(candidate, fontSize: fontSize) <= maxWidth || current.isEmpty {
        current = candidate
      } else {
        lines.append(current)
        current = word
      }

      if lines.count == maxLines {
        break
      }
    }

    if !current.isEmpty, lines.count < maxLines {
      lines.append(current)
    }

    if lines.count == maxLines,
       let last = lines.indices.last,
       words.joined(separator: " ").hasPrefix(lines.joined(separator: " ")) == false {
      lines[last] = String(lines[last].prefix(max(0, lines[last].count - 1))) + "..."
    }

    return lines
  }

  private func lineWidth(_ text: String, fontSize: CGFloat) -> CGFloat {
    let font = CTFontCreateWithName("HelveticaNeue-Bold" as CFString, fontSize, nil)
    let attributes = [kCTFontAttributeName: font] as CFDictionary
    let attributed = CFAttributedStringCreate(nil, text as CFString, attributes)
    let line = CTLineCreateWithAttributedString(attributed!)
    return CTLineGetBoundsWithOptions(line, [.useGlyphPathBounds, .useOpticalBounds]).width
  }
}
