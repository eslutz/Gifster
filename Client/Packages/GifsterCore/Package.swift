// swift-tools-version: 6.0

import PackageDescription

let package = Package(
  name: "GifsterCore",
  platforms: [
    .iOS("26.5"),
    .macOS("15.0")
  ],
  products: [
    .library(
      name: "GifsterCore",
      targets: ["GifsterCore"]
    )
  ],
  targets: [
    .target(
      name: "GifsterCore"
    ),
    .testTarget(
      name: "GifsterCoreTests",
      dependencies: ["GifsterCore"]
    )
  ]
)
