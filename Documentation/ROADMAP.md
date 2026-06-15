# Roadmap

## V1 Foundation

- iMessage extension for prompt entry, optional source image, caption mode, GIF preview, and attachment insertion.
- Containing app for explanation, privacy, settings, and local history.
- Shared Swift package for request planning, backend client, image preprocessing, caption rendering, GIF creation, and history.
- Active job persistence and resume across extension launches.
- MP4 ingestion with AVFoundation frame extraction.
- ASP.NET Core Minimal API backend with Native AOT settings, fake provider, provider abstraction, job polling, App Attest enforcement, and xUnit coverage.
- Azure Container Apps infrastructure for nonprod/prod, with Queue Storage, Blob Storage, Table Storage, Key Vault, managed identity, and scale-to-zero defaults.
- Manual nonprod deployment workflow with resource-group-scoped Azure OIDC setup and backend smoke testing.
- Dry-run-first Azure OIDC setup helper for both `nonprod` and `prod` GitHub environments.
- Guarded manual production deployment workflow with immutable image tags, required App Attest/provider settings, disabled demo bypass, and health checks.

## Future Items

- Real AI media provider integrations.
- Production App Attest configuration, real production deployment evidence, and physical-device validation.
- Production signing, App Store Connect screenshots, and final submission evidence.
- Multiple provider adapters behind the same backend contract.
- Image Playground optional source-image path if the spike succeeds.
- Sticker mode as a future export option.
- Saved generation history with richer preview thumbnails.
- Subscriptions or credits.
- Better moderation and reporting.
- Cloud sync if needed.
- More caption styles.
- More GIF styles and presets.
