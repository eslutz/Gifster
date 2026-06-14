# Architecture Overview

Gifster is split into five bounded areas:

1. Messages extension: prompt entry, image selection, caption editing, progress, preview, and attachment insertion.
2. Containing app: onboarding, privacy explanation, local history, clear-history control, and development settings.
3. Shared Swift package: request models, prompt planning facade, backend client, image preprocessing, frame rendering, GIF encoding, and local history.
4. Backend service: request validation, safety checks, provider credential isolation, provider abstraction, job state, and temporary result URLs.
5. Documentation and demo flow: fake provider, local backend, repeatable tests, and roadmap.

## AI Boundary

Apple Foundation Models are the local planning layer for:

- Prompt cleanup.
- Prompt expansion.
- Caption suggestions.
- Converting messy user intent into `StructuredGenerationRequest`.
- Understanding selected images when the SDK supports that from the extension context.

External AI media providers are only used behind the backend for:

- Text-to-animation.
- Image-to-animation.
- Producing an intermediate motion asset such as MP4 or a frame sequence.

The app always owns final GIF creation and caption rendering.

## Provider-Neutral Backend Contract

The app submits `POST /v1/generations` with:

- `mode`: `text_to_gif` or `image_to_gif`.
- `cleanedPrompt` and `expandedPrompt`.
- `negativePrompt` instructing the provider not to render readable text.
- `caption.renderLocally = true`.
- `options` such as size, loop length, style preset, and motion intensity.
- Optional `sourceImage` with metadata-stripped JPEG bytes encoded as base64.

The backend returns a job id and status URL. The app polls `GET /v1/generations/:jobId`. On success the backend returns a temporary `downloadUrl`. The demo backend serves a frame sequence JSON result; real adapters can serve MP4 or frame-sequence assets under the same app-facing contract.

## iMessage Extension Behavior

The extension treats Messages as a short-lived surface:

- Backend owns long-running job state.
- The extension stores recent local output and reloads history when reopened.
- The current scaffold exposes a polling loop and history restore path.
- A production continuation should persist active job ids so a reopened extension resumes polling instead of creating duplicate jobs.

Messages insertion is limited to `MSConversation.insertAttachment`. Gifster does not auto-send. Sticker APIs are not used in v1.

## GIF Generation Pipeline

1. User input becomes `GenerationIntent`.
2. Prompt planning produces `StructuredGenerationRequest`.
3. Backend job returns a generated motion asset.
4. The app turns the motion asset into frames.
5. The app renders visible caption text onto frames locally.
6. ImageIO writes an animated GIF.
7. The extension previews the file and inserts it into Messages as an attachment.

The fake provider returns `frame-sequence-v1` so the pipeline is testable without video decoding. A real MP4 adapter should add an AVFoundation frame extraction path before the same caption and GIF rendering steps.
