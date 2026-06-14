# Product Overview

Gifster is a GIPHY-style iMessage app for making custom animated GIFs instead of searching a public content library. The primary surface is the Messages extension. The containing app explains the product, exposes privacy and development settings, and shows local generation history.

## Core Flows

### Text-to-GIF

1. User opens Gifster from the Messages app drawer.
2. User enters a prompt.
3. The app plans the request locally with Apple Foundation Models where available.
4. The app submits a structured request to the Gifster backend.
5. The backend validates, moderates, and submits the job to a configured AI media provider.
6. The app polls backend job status, downloads the provider motion result, renders any caption locally, creates the final GIF, previews it, and inserts it into the Messages compose field.

### Image-to-GIF

1. User opens Gifster from Messages.
2. User selects a static image using a picker scoped to user-selected media.
3. The app downscales and strips metadata from the image before upload.
4. The app plans the animation request locally where available.
5. The backend submits the processed request and source image to a provider.
6. The app downloads the generated motion result, creates the final GIF locally, previews it, and inserts it as an attachment.

### Caption Modes

- No caption.
- Use my text.
- Suggest text with AI.

Explicit user captions are preserved unless they fail safety checks or exceed the local rendering limit. AI-suggested captions are generated locally when Apple Foundation Models are available. Users can review, choose, and edit suggested captions before final rendering. Caption edits only re-render the final GIF and do not require another AI media generation request.

## V1 Non-Goals

- Sticker mode.
- Sticker APIs.
- Public GIF gallery.
- Social feed.
- Browsing other users' content.
- Direct AI provider calls from the iOS app.
- Auto-send behavior.
- Dependency on Image Playground.
- Dependency on Sora or any single AI provider.
