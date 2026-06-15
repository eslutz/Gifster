import Foundation

public enum GeneratedMotionAsset: Sendable {
  case frameSequence(FrameSequenceAsset)
  case mp4(URL)
}
