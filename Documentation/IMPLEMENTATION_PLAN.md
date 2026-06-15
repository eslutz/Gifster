# Gifster Implementation Plan

## Phase 1: Scaffold and Demo Loop

Status: implemented in this scaffold.

- Generate the Xcode project from `Client/project.yml`.
- Keep shared logic in `Client/Packages/GifsterCore`.
- Build the containing app with onboarding, privacy, history, and backend settings.
- Build the Messages extension with compact and expanded SwiftUI surfaces.
- Use PhotosPicker for user-selected images only.
- Submit all generation jobs through the backend.
- Use the ASP.NET Core demo provider to return a frame sequence.
- Render captions locally and produce the final GIF with ImageIO.
- Insert the GIF into Messages with attachment insertion.

## Phase 2: Foundation Models Integration

- `FoundationModelsPromptPlanner` now uses Apple Foundation Models guided generation on iOS/macOS 26+ when `SystemLanguageModel.default.isAvailable`.
- Keep `LocalPromptPlanner` as the deterministic fallback for Simulator, CI, unsupported OS versions, unavailable Apple Intelligence, or local-model errors.
- Maintain structured output schemas for prompt planning and caption suggestions.
- Add image-understanding support only where Apple exposes it for extension-safe use.
- Add graceful unavailable states for devices without Apple Intelligence support.

## Phase 3: Production Backend

- Deploy the ASP.NET Core Minimal API with Native AOT to Azure Container Apps.
- Require App Attest for deployed environments. The backend fails closed by default, supports an explicit `GIFSTER_APP_ATTEST_DEMO_BYPASS=true` path for local/nonprod smoke testing only, and verifies real App Attest attestation objects when the app identifier and Apple App Attest root certificate are configured.
- Use Azure Table Storage for durable job state and App Attest challenge/session state.
- Use Azure Queue Storage for long-running provider orchestration, including retrying transient provider/result-store failures through queue visibility semantics.
- Store provider outputs and temporary download assets in Azure Blob Storage.
- Run the public API and queue worker as separate Azure Container Apps from the same image.
- Add provider adapter interfaces for text-to-animation, image-to-animation, and result download.
- Add request and result retention policies.
- Add operational logs without storing prompt or image content longer than necessary.
- Keep `infra/main.subscription.bicep` as the bootstrap entry point for creating `nonprod` and `prod` resource groups, and keep `infra/main.bicep` as the source of truth for Container Apps, storage, Key Vault, managed identity, and role assignments. Use resource-group-scope deployments for normal environment updates so GitHub Actions identities can be scoped per environment.

## Phase 4: Real Provider Adapter

- Keep the fake provider as the default nonprod/demo adapter until the first paid media provider is selected.
- Use `GIFSTER_PROVIDER_ADAPTER=external-http` for the first provider-compatible gateway or vendor-specific wrapper after provider selection.
- Convert `StructuredGenerationRequest` into provider-specific parameters.
- Keep provider credentials server-side only.
- Request MP4 or frame sequence output, not final captioned GIF output.
- Add backend contract tests so provider swapping does not change the iOS app.

## Phase 5: GIF Quality

- MP4 frame extraction with AVFoundation is implemented through `MP4FrameExtractor`.
- The app now handles frame-sequence JSON and direct MP4 result payloads through `GeneratedMotionAsset`.
- GIF rendering enforces Messages-oriented frame-count, dimension, and file-size limits.
- Caption rendering wraps and fits longer text locally before GIF creation.
- Caption edits still re-render locally without requiring another provider generation request.

## Phase 6: App Store Readiness

- Maintain the App Store readiness checklist in `Documentation/APP_STORE_READINESS.md`.
- Finalize privacy policy and in-app disclosure.
- Configure production App Attest app identifier/root certificate values and validate the flow on a physical device.
- Add production signing, app groups, and Messages extension metadata.
- User-facing error copy is implemented and covered for provider downtime, unavailable local models, network failures, moderation rejections, and App Attest unavailable states.
- Keep backend tests on xUnit and shared Swift package tests on Swift Testing.
- Keep UI tests for the containing app in `Client/Tests/GifsterUITests`.
- Manually test Messages compact and expanded modes on physical devices.
- Prepare App Review notes documenting attachment insertion, no auto-send behavior, and backend-mediated provider calls.
