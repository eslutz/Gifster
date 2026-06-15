import Foundation

public struct FrameSequenceAsset: Codable, Equatable, Sendable {
  public var format: String
  public var width: Int
  public var height: Int
  public var frames: [FrameSpec]
  public var promptEcho: String

  public init(format: String, width: Int, height: Int, frames: [FrameSpec], promptEcho: String) {
    self.format = format
    self.width = width
    self.height = height
    self.frames = frames
    self.promptEcho = promptEcho
  }
}

public struct FrameSpec: Codable, Equatable, Sendable {
  public var index: Int
  public var duration: Double
  public var backgroundHex: String
  public var accentHex: String
  public var motionOffset: Double

  public init(
    index: Int,
    duration: Double,
    backgroundHex: String,
    accentHex: String,
    motionOffset: Double
  ) {
    self.index = index
    self.duration = duration
    self.backgroundHex = backgroundHex
    self.accentHex = accentHex
    self.motionOffset = motionOffset
  }
}
