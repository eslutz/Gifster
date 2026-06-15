import AVFoundation
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

  @Test("MP4 source extracts frames for local GIF rendering")
  func extractsFramesFromMP4() async throws {
    let movieURL = try makeTestMovie()
    let frames = try await MP4FrameExtractor(
      frameCount: 4,
      maximumPixelDimension: 160
    ).extractFrames(from: movieURL, caption: "MP4")
    let outputURL = FileManager.default.temporaryDirectory
      .appending(path: "gifster-mp4-test-\(UUID().uuidString).gif")

    try GIFRenderer().render(frames: frames, to: outputURL)

    let data = try Data(contentsOf: outputURL)
    let source = try #require(CGImageSourceCreateWithData(data as CFData, nil))
    #expect(CGImageSourceGetCount(source) == frames.count)
    #expect(frames.count == 4)
  }

  @Test("Motion asset renderer handles frame sequence assets")
  func motionAssetRendererHandlesFrameSequences() async throws {
    let asset = DemoFrameSequence.make(prompt: "demo")
    let frames = try await MotionAssetFrameRenderer().renderFrames(
      from: .frameSequence(asset),
      caption: "DEMO"
    )

    #expect(frames.count == asset.frames.count)
  }

  @Test("GIF renderer downsamples dimensions and frame count for Messages limits")
  func gifRendererDownsamplesForMessagesLimits() throws {
    let asset = DemoFrameSequence.make(prompt: "limits")
    let frames = try FrameSequenceRenderer().renderFrames(from: asset, caption: nil)
    let outputURL = FileManager.default.temporaryDirectory
      .appending(path: "gifster-limits-test-\(UUID().uuidString).gif")

    try GIFRenderer().render(
      frames: frames,
      to: outputURL,
      options: GIFRenderOptions(maxPixelDimension: 96, maxFrameCount: 4)
    )

    let data = try Data(contentsOf: outputURL)
    let source = try #require(CGImageSourceCreateWithData(data as CFData, nil))
    #expect(CGImageSourceGetCount(source) == 4)
    let image = try #require(CGImageSourceCreateImageAtIndex(source, 0, nil))
    #expect(max(image.width, image.height) <= 96)
  }

  @Test("GIF renderer preserves total duration when sampling frames")
  func gifRendererPreservesDurationWhenSamplingFrames() throws {
    let durations = [0.1, 0.2, 0.3, 0.4, 0.5, 0.6]
    let frames = try durations.enumerated().map { index, duration in
      GIFFrame(
        image: try testImage(red: CGFloat(index + 1) / CGFloat(durations.count + 1)),
        duration: duration
      )
    }
    let outputURL = FileManager.default.temporaryDirectory
      .appending(path: "gifster-duration-test-\(UUID().uuidString).gif")

    try GIFRenderer().render(
      frames: frames,
      to: outputURL,
      options: GIFRenderOptions(maxPixelDimension: 96, maxFrameCount: 3)
    )

    let data = try Data(contentsOf: outputURL)
    let source = try #require(CGImageSourceCreateWithData(data as CFData, nil))
    let renderedDurations = try (0..<CGImageSourceGetCount(source)).map {
      try gifDelay(at: $0, in: source)
    }

    #expect(renderedDurations.count == 3)
    #expect(abs(renderedDurations.reduce(0, +) - durations.reduce(0, +)) < 0.05)
    #expect(abs(renderedDurations[0] - 0.3) < 0.05)
    #expect(abs(renderedDurations[1] - 0.7) < 0.05)
    #expect(abs(renderedDurations[2] - 1.1) < 0.05)
  }

  @Test("Caption renderer wraps and fits long captions")
  func captionRendererWrapsAndFitsLongCaptions() {
    let renderer = CaptionRenderer(style: CaptionRenderingStyle(
      fontSize: 32,
      minimumFontSize: 16,
      bottomInset: 18,
      horizontalInset: 16,
      maxLines: 3
    ))

    let layout = renderer.layoutCaption(
      "This caption should wrap cleanly without clipping",
      canvasSize: CGSize(width: 180, height: 120)
    )

    #expect(layout.lines.count > 1)
    #expect(layout.lines.count <= 3)
    #expect(layout.fontSize <= 32)
    #expect(layout.textBounds.width <= 148)
  }

  private func makeTestMovie() throws -> URL {
    let url = FileManager.default.temporaryDirectory
      .appending(path: "gifster-test-source-\(UUID().uuidString).mp4")
    let writer = try AVAssetWriter(outputURL: url, fileType: .mp4)
    let settings: [String: Any] = [
      AVVideoCodecKey: AVVideoCodecType.h264,
      AVVideoWidthKey: 160,
      AVVideoHeightKey: 120
    ]
    let input = AVAssetWriterInput(mediaType: .video, outputSettings: settings)
    let adaptor = AVAssetWriterInputPixelBufferAdaptor(
      assetWriterInput: input,
      sourcePixelBufferAttributes: [
        kCVPixelBufferPixelFormatTypeKey as String: kCVPixelFormatType_32ARGB,
        kCVPixelBufferWidthKey as String: 160,
        kCVPixelBufferHeightKey as String: 120
      ]
    )

    writer.add(input)
    writer.startWriting()
    writer.startSession(atSourceTime: .zero)

    for index in 0..<4 {
      while !input.isReadyForMoreMediaData {
        Thread.sleep(forTimeInterval: 0.01)
      }

      let buffer = try pixelBuffer(width: 160, height: 120, frameIndex: index)
      let time = CMTime(value: CMTimeValue(index), timescale: 2)
      adaptor.append(buffer, withPresentationTime: time)
    }

    input.markAsFinished()
    let semaphore = DispatchSemaphore(value: 0)
    writer.finishWriting {
      semaphore.signal()
    }
    semaphore.wait()

    if let error = writer.error {
      throw error
    }

    return url
  }

  private func pixelBuffer(width: Int, height: Int, frameIndex: Int) throws -> CVPixelBuffer {
    var maybeBuffer: CVPixelBuffer?
    let status = CVPixelBufferCreate(
      kCFAllocatorDefault,
      width,
      height,
      kCVPixelFormatType_32ARGB,
      nil,
      &maybeBuffer
    )
    guard status == kCVReturnSuccess, let buffer = maybeBuffer else {
      throw GifsterError.mediaRenderingFailed(message: "Could not create test pixel buffer.")
    }

    CVPixelBufferLockBaseAddress(buffer, [])
    defer { CVPixelBufferUnlockBaseAddress(buffer, []) }

    guard let baseAddress = CVPixelBufferGetBaseAddress(buffer) else {
      throw GifsterError.mediaRenderingFailed(message: "Could not address test pixel buffer.")
    }

    let bytesPerRow = CVPixelBufferGetBytesPerRow(buffer)
    let color = UInt8(60 + frameIndex * 40)
    for row in 0..<height {
      let rowPointer = baseAddress.advanced(by: row * bytesPerRow)
      let pixels = rowPointer.bindMemory(to: UInt8.self, capacity: bytesPerRow)
      for column in 0..<width {
        let offset = column * 4
        pixels[offset] = 255
        pixels[offset + 1] = color
        pixels[offset + 2] = UInt8(row % 255)
        pixels[offset + 3] = UInt8(column % 255)
      }
    }

    return buffer
  }

  private func testImage(red: CGFloat) throws -> CGImage {
    guard
      let context = CGContext(
        data: nil,
        width: 24,
        height: 24,
        bitsPerComponent: 8,
        bytesPerRow: 0,
        space: CGColorSpace(name: CGColorSpace.sRGB)!,
        bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue
      )
    else {
      throw GifsterError.mediaRenderingFailed(message: "Could not create test image.")
    }

    context.setFillColor(CGColor(red: red, green: 0.3, blue: 0.6, alpha: 1))
    context.fill(CGRect(x: 0, y: 0, width: 24, height: 24))

    guard let image = context.makeImage() else {
      throw GifsterError.mediaRenderingFailed(message: "Could not finish test image.")
    }

    return image
  }

  private func gifDelay(at index: Int, in source: CGImageSource) throws -> Double {
    let properties = try #require(
      CGImageSourceCopyPropertiesAtIndex(source, index, nil) as? [String: Any]
    )
    let gifProperties = try #require(
      properties[kCGImagePropertyGIFDictionary as String] as? [String: Any]
    )
    let delay = (gifProperties[kCGImagePropertyGIFUnclampedDelayTime as String] as? NSNumber)
      ?? (gifProperties[kCGImagePropertyGIFDelayTime as String] as? NSNumber)

    return try #require(delay).doubleValue
  }
}
