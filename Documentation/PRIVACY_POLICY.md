# GifForge Privacy Policy

Effective date: June 15, 2026

GifForge is an iMessage app for creating custom animated GIFs from prompts and optional user-selected media. This policy explains what data GifForge handles, why it is used, and how users can control local data.

## Data GifForge Handles

GifForge may process:

- Text prompts entered by the user.
- Optional images, GIFs, videos, or Live Photo paired MOV files selected by the user.
- Optional caption text entered or selected by the user.
- Generated MP4 source videos, generated GIF files, and local generation history.
- Backend job identifiers, status URLs, and temporary result URLs.
- Internal GifForge account identifiers, optional Sign in with Apple recovery identifiers, private relay email metadata when Apple provides it, backend session records, and refresh-token metadata.
- Apple In-App Purchase transaction identifiers, product ids, credit grants, credit reservations, debit ledger entries, refund/reversal ledger entries, and generation ownership records.
- Development settings such as the backend base URL and App Attest toggle.

GifForge does not provide a public GIF gallery, social feed, browsing of other users' content, or direct AI provider access from the iOS app.

## User-Selected Media

GifForge uses only media selected by the user. The app does not request broad photo library access for v1.

Before upload, selected still images may be downscaled and rewritten as JPEG data. This process is intended to reduce payload size and remove original image metadata. GIF, MP4, MOV, and Live Photo workflows require the motion source. For Live Photos, GifForge uses the paired MOV file for animation workflows; the still image alone is not enough for Live Photo video-to-video generation.

## Backend and AI Providers

Prompts, optional processed media, and structured generation requests are sent to the developer-operated GifForge backend. The backend validates requests, applies safety checks, hides provider credentials, submits jobs to configured AI media providers, tracks long-running jobs, downloads generated MP4 assets from providers, stores generated videos in GifForge-controlled temporary storage, and returns temporary result URLs to the app.

External AI media providers are used only through the backend. The iOS app does not store or ship external provider credentials.

## Account and Purchases

GifForge creates a local backend account automatically. Sign in with Apple is optional and is used for account recovery. The backend stores an internal user id, the Apple app account token used with StoreKit, and backend session metadata. If you enable Apple recovery, the backend also stores Apple's stable account subject and private relay email metadata when Apple provides it.

GifForge uses Apple In-App Purchase consumable credit packs as the only payment method. Purchase prices and payment handling are managed by Apple. The backend verifies signed StoreKit transaction payloads before granting credits and stores transaction identifiers, product ids, payload hashes, credit ledger entries, and refund/reversal records. Raw Apple identity tokens, raw StoreKit signed transaction payloads, bearer tokens, refresh tokens, and App Attest session tokens are not intentionally logged or stored in plaintext by the backend.

## Local Processing

Where available, Apple Foundation Models are used locally for prompt cleanup, prompt expansion, structured request planning, and caption suggestions. If local models are unavailable, GifForge uses deterministic local fallback logic for planning.

Captions are rendered locally into the final GIF. GifForge does not ask external media providers to render readable caption text into the generated animation.

## Local Storage

GifForge stores generated GIFs, recent generation history, and resumable active-job metadata in the shared app container used by the containing app and Messages extension.

Backend access and refresh tokens are stored in the shared Keychain used by the containing app and Messages extension. They are not stored in `UserDefaults` or files.

Users can clear local generation history from the containing app. Clearing local history also clears resumable active-job metadata.

## Retention

The app stores generated GIFs locally only as needed for recent history and user sharing.

After validation, safety checks, and provider submission, the backend stores minimized generation job state. It keeps the structured prompt, caption mode, source-media metadata, source-image dimensions/context, job status, provider attempt metadata, retry metadata, and result-link metadata needed to complete the job, but clears raw prompt text, visible caption text, processed source-media bytes, and processed source-image bytes from persisted job records. SQL keeps generation ownership and credit reservation/ledger records needed for account security, billing integrity, refund handling, abuse prevention, and support. The backend does not store raw source media for automatic provider fallback. If a provider fails and another compatible provider/model is available, GifForge reports that retry is available; the app keeps the original request media locally only in the active-generation snapshot, prompts the user, and resubmits only if the user chooses to retry. Local retry material is cleared when generation succeeds, when the job expires or local cleanup runs, or when the user declines retry. Deployed defaults expire remaining job records after 24 hours and return HTTP `410 Gone` for expired status or result requests. Temporary generated MP4/provider result blobs are deleted by Azure Storage lifecycle policy after 2 days as a backstop.

## Tracking

GifForge does not use data for tracking and does not include third-party advertising or analytics SDKs in this scaffold.

## Security

Deployed backends are intended to require backend bearer auth plus App Attest-backed request integrity for protected generation APIs. The backend validates Apple identity tokens, backend access tokens, StoreKit signed transactions, App Store Server Notifications, App Attest assertions, generation ownership, and credit availability. Provider credentials are stored server-side, and Azure deployments use managed identity for Azure resource access where configured.

## User Controls

Users can:

- Choose whether to provide an image, GIF, video, or Live Photo.
- Review and edit caption text before final GIF rendering.
- Insert generated GIFs manually into Messages.
- Enable optional Sign in with Apple recovery and purchase credits through Apple In-App Purchase.
- Delete local history from the containing app.

GifForge inserts GIFs into the Messages compose field only. Messages requires the user to send manually.

## Contact

For privacy questions or deletion requests related to backend data, use the support contact published with the App Store listing.
