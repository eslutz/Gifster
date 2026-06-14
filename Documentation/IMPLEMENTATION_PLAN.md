# Gifster Implementation Plan

## Phase 1: Scaffold and Demo Loop

Status: implemented in this scaffold.

- Generate the Xcode project from `project.yml`.
- Keep shared logic in `Packages/GifsterCore`.
- Build the containing app with onboarding, privacy, history, and backend settings.
- Build the Messages extension with compact and expanded SwiftUI surfaces.
- Use PhotosPicker for user-selected images only.
- Submit all generation jobs through the backend.
- Use the fake backend provider to return a frame sequence.
- Render captions locally and produce the final GIF with ImageIO.
- Insert the GIF into Messages with attachment insertion.

## Phase 2: Foundation Models Integration

- Replace the deterministic fallback behavior in `FoundationModelsPromptPlanner` with real Apple Foundation Models guided generation once the final iOS 26.5 SDK integration details are locked.
- Add structured output schemas for prompt planning and caption suggestions.
- Add image-understanding support only where Apple exposes it for extension-safe use.
- Add graceful unavailable states for devices without Apple Intelligence support.

## Phase 3: Production Backend

- Add authentication and device/app attestation as appropriate.
- Replace the in-memory job store with durable job storage.
- Add signed temporary download URLs.
- Add provider adapter interfaces for text-to-animation, image-to-animation, and result download.
- Add request and result retention policies.
- Add operational logs without storing prompt or image content longer than necessary.

## Phase 4: Real Provider Adapter

- Implement one real provider adapter behind the backend abstraction.
- Convert `StructuredGenerationRequest` into provider-specific parameters.
- Keep provider credentials server-side only.
- Request MP4 or frame sequence output, not final captioned GIF output.
- Add backend contract tests so provider swapping does not change the iOS app.

## Phase 5: GIF Quality

- Add MP4 frame extraction with AVFoundation.
- Tune palette, frame rate, loop duration, and file-size limits for Messages.
- Add caption layout styles and automatic line fitting.
- Add local re-render when users edit captions after generation.
- Add file-size checks and downsampling before insertion.

## Phase 6: App Store Readiness

- Finalize privacy policy and in-app disclosure.
- Add production signing, app groups, and Messages extension metadata.
- Add error copy for provider downtime, unavailable local models, network failures, and moderation rejections.
- Add UI tests for the containing app.
- Manually test Messages compact and expanded modes on physical devices.
- Prepare App Review notes documenting attachment insertion, no auto-send behavior, and backend-mediated provider calls.
