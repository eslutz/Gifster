# Gifster Privacy Policy

Effective date: June 15, 2026

Gifster is an iMessage app for creating custom animated GIFs from prompts and optional user-selected images. This policy explains what data Gifster handles, why it is used, and how users can control local data.

## Data Gifster Handles

Gifster may process:

- Text prompts entered by the user.
- Optional images selected by the user.
- Optional caption text entered or selected by the user.
- Generated GIF files and local generation history.
- Backend job identifiers, status URLs, and temporary result URLs.
- Development settings such as the backend base URL and App Attest toggle.

Gifster does not provide a public GIF gallery, social feed, browsing of other users' content, or direct AI provider access from the iOS app.

## User-Selected Images

Gifster uses only images selected by the user. The app does not request broad photo library access for v1.

Before upload, selected images are downscaled and rewritten as JPEG data. This process is intended to reduce payload size and remove original image metadata.

## Backend and AI Providers

Prompts, optional processed images, and structured generation requests are sent to the developer-operated Gifster backend. The backend validates requests, applies safety checks, hides provider credentials, submits jobs to configured AI media providers, tracks long-running jobs, and returns temporary result URLs to the app.

External AI media providers are used only through the backend. The iOS app does not store or ship external provider credentials.

## Local Processing

Where available, Apple Foundation Models are used locally for prompt cleanup, prompt expansion, structured request planning, and caption suggestions. If local models are unavailable, Gifster uses deterministic local fallback logic for planning.

Captions are rendered locally into the final GIF. Gifster does not ask external media providers to render readable caption text into the generated animation.

## Local Storage

Gifster stores generated GIFs, recent generation history, and resumable active-job metadata in the shared app container used by the containing app and Messages extension.

Users can clear local generation history from the containing app. Clearing local history also clears resumable active-job metadata.

## Retention

The app stores generated GIFs locally only as needed for recent history and user sharing.

The backend stores generation job metadata, prompts, selected source-image payloads, and result links with an expiration timestamp. Deployed defaults expire these job records after 24 hours and return HTTP `410 Gone` for expired status or result requests. Temporary provider result and source-image blobs are deleted by Azure Storage lifecycle policy after 2 days.

## Tracking

Gifster does not use data for tracking and does not include third-party advertising or analytics SDKs in this scaffold.

## Security

Deployed backends are intended to require App Attest-backed access. The backend validates requests, applies moderation checks, stores provider credentials server-side, and uses managed identity for Azure resource access where configured.

## User Controls

Users can:

- Choose whether to provide an image.
- Review and edit caption text before final GIF rendering.
- Insert generated GIFs manually into Messages.
- Delete local history from the containing app.

Gifster inserts GIFs into the Messages compose field only. Messages requires the user to send manually.

## Contact

For privacy questions or deletion requests related to backend data, use the support contact published with the App Store listing.
