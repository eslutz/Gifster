# Development and Demo Setup

## Prerequisites

- Xcode 26.5 or later for iOS app and extension builds.
- XcodeGen 2.45 or later.
- Swift 6.
- Node 24 or later for the fake backend.

The current scaffold was generated and build-checked on Xcode 26.5 with the iOS 26.5 SDK.

## Generate the Xcode Project

```bash
xcodegen generate
open Gifster.xcodeproj
```

Select the `Gifster` scheme. Configure signing for the app and extension bundle ids:

- `dev.ericslutz.Gifster`
- `dev.ericslutz.Gifster.MessagesExtension`

Update the app-group identifier if your Apple Developer account requires a different prefix.

## Run Shared Swift Tests

```bash
cd Packages/GifsterCore
swift test --scratch-path /private/tmp/gifster-swiftpm
```

## Run the Fake Backend

```bash
cd Backend
npm test
npm run dev
```

The backend listens at `http://127.0.0.1:8787`.

## End-to-End Demo Flow

1. Start the fake backend with `npm run dev`.
2. Launch the containing app once and confirm the backend URL in Settings.
3. Open Messages, select the Gifster iMessage app, and enter a prompt.
4. Optionally add an image with the Photos picker.
5. Select a caption mode.
6. Tap Generate.
7. The extension plans a structured request, submits a fake backend job, polls until completion, downloads a fake frame sequence, renders a local GIF, and shows a preview.
8. Tap Insert to add the GIF to the Messages compose field.
9. Send manually from Messages.
