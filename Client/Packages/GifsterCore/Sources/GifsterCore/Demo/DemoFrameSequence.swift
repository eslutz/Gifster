import Foundation

public enum DemoFrameSequence {
  public static func make(prompt: String = "demo") -> FrameSequenceAsset {
    let colors = ["#006D77", "#83C5BE", "#EDF6F9", "#FFDDD2", "#E29578", "#2A9D8F"]
    let frames = (0..<18).map { index in
      FrameSpec(
        index: index,
        duration: 0.08,
        backgroundHex: colors[index % colors.count],
        accentHex: colors[(index + 3) % colors.count],
        motionOffset: sin(Double(index) / 17.0 * Double.pi * 2.0) * 48.0
      )
    }

    return FrameSequenceAsset(
      format: "frame-sequence-v1",
      width: 480,
      height: 360,
      frames: frames,
      promptEcho: prompt
    )
  }
}
