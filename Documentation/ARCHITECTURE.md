# Architecture Overview

GifForge is split into five bounded areas:

1. Messages extension: prompt entry, image selection, caption editing, progress, preview, and attachment insertion.
2. Containing app: onboarding, privacy explanation, local history, clear-history control, and development settings.
3. Shared Swift package: request models, prompt planning facade, backend client, image preprocessing, frame rendering, GIF encoding, and local history.
4. Backend service: ASP.NET Core Minimal API with Native AOT, App Attest enforcement, request validation, safety checks, provider credential isolation, provider abstraction, durable job state, and temporary result URLs.
5. Documentation and demo flow: fake provider, local backend, repeatable tests, and roadmap.

## AI Boundary

Apple Foundation Models are the local planning layer for:

- Prompt cleanup.
- Prompt expansion.
- Caption suggestions.
- Converting messy user intent into `StructuredGenerationRequest`.
- Understanding selected images when the SDK supports that from the extension context.

External AI media providers are only used behind the backend for:

- Text-to-video.
- Image-to-video.
- Video-to-video for GIF, MP4, MOV, and Live Photo paired MOV inputs.
- Producing a short silent MP4 or a test-only frame sequence.

The app always owns final GIF creation and caption rendering.

## Provider-Neutral Backend Contract

The app submits `POST /v1/generations` with:

- `mode`: `text_to_gif`, `image_to_gif`, or `video_to_gif`.
- `cleanedPrompt` and `expandedPrompt`.
- `negativePrompt` instructing the provider not to render readable text.
- `caption.renderLocally = true`.
- `options` such as size, loop length, style preset, and motion intensity.
- Optional `sourceMedia` for JPEG, PNG, HEIC/HEIF, GIF, MP4, MOV, or Live Photo paired MOV uploads.
- Optional `sourceImage` with metadata-stripped JPEG bytes encoded as base64 for the original processed-image path.
- Optional `sourceImageContext` with app-derived, metadata-only dimensions, orientation, aspect ratio, and summary text for provider planning. Semantic image understanding remains gated on an extension-safe Apple API path.

The backend returns a job id, status URL, and expiration timestamp. The app polls `GET /v1/generations/:jobId`. On success the backend returns a temporary `downloadUrl`. Expired status and result requests return HTTP `410 Gone`. The demo provider serves a frame sequence JSON result; real adapters can serve MP4 or frame-sequence assets under the same app-facing contract.

The runtime backend uses the direct video provider router with `IVideoGenerationProvider` implementations for fal.ai and Luma. It classifies each request into text-to-video, image-to-video, or video-to-video, sorts compatible enabled C# model catalog entries by effective estimated cost, and submits to the cheapest candidate first. fal.ai Wan 2.2 defaults are the low-cost primary path; Luma Ray 3.2 defaults are retry candidates. Provider/model identity lives in C#; Azure App Configuration controls provider enablement and cost overrides, while Azure Key Vault stores provider API keys. Provider result URLs are never returned to clients: the backend downloads the generated MP4, stores it in app-controlled storage, and returns the stable GifForge result URL.

Provider enablement and model costs are configuration-driven. Deployed environments should source non-secret values from Azure App Configuration and provider API keys from Azure Key Vault. The backend uses managed identity through `AZURE_APP_CONFIG_ENDPOINT`, `AZURE_KEY_VAULT_ENDPOINT`/`GIFFORGE_KEY_VAULT_URI`, and `AZURE_CLIENT_ID`. OpenTelemetry traces and metrics are enabled for ASP.NET Core and provider HTTP calls; set `OTEL_EXPORTER_OTLP_ENDPOINT` to export telemetry.

For media-backed jobs, the backend does not retain retry material. It validates the uploaded source media for the current provider submission and then persists only sanitized job state with raw `sourceMedia` and `sourceImage` bytes removed. If result retrieval returns a permanent provider failure, the job is marked failed with retry metadata (`retryAvailable`, `retryReason`, and `retryOfJobId`) when another compatible configured provider/model remains. The iOS client keeps the original request media locally in its active-generation snapshot, prompts the user, and resubmits with `retryOfJobId` only after confirmation. The compact attempt state (`AttemptCount`, `AttemptedProviders`, `AttemptedModelIds`, and current `ProviderModelId`) prevents retry loops and is capped by `GIFFORGE_GENERATION_MAX_ATTEMPTS`.

## Auth, IAP, and Credits

Sign in with Apple is the only account sign-in method. The containing app performs Sign in with Apple with a nonce, sends the Apple identity token to `POST /v1/auth/apple`, and stores the returned backend access/refresh tokens in the shared Keychain. The Messages extension uses the shared Keychain token to call protected APIs; it does not own purchase UI.

Apple In-App Purchase is the only payment provider. V1 uses consumable credit packs with product ids `dev.ericslutz.gifforge.credits.10`, `dev.ericslutz.gifforge.credits.25`, and `dev.ericslutz.gifforge.credits.60`. The client sends StoreKit's signed transaction JWS to `POST /v1/iap/transactions`; the backend verifies the JWS signature/certificate chain, bundle id, product id, transaction type, app account token, and revocation state before granting credits. StoreKit transactions are finished client-side only after the backend confirms the idempotent grant.

Azure SQL is the source of truth for user identity, refresh tokens, IAP products, IAP transactions, credit reservations, immutable ledger entries, generation ownership, and auth/purchase audit records. Azure Table Storage remains the operational store for generation job state plus short-lived App Attest challenge/session state.

Credit accounting uses reservation then capture. When a generation request is accepted, SQL reserves one credit and records generation ownership before queueing the job. The reservation reduces available balance, preventing a user from starting more concurrent jobs than they can afford. The worker captures the reservation only after a usable provider result is stored. Terminal provider/backend failure or job expiry releases the reservation, so no compensating credit is needed. App Store refund/revoke notifications insert reversal ledger entries; if already-spent credits make the balance negative, new reservations are blocked until the available balance is non-negative again.

## Backend Runtime

GifForge targets ASP.NET Core Minimal API with Native AOT on Azure Container Apps for production. The API process should stay stateless and low-memory so the consumption workload profile can scale to zero and scale out efficiently.

Production Azure services should be split by responsibility:

- Azure Container Apps hosts the public Minimal API and a separate queue worker app from the same container image.
- Azure Queue Storage carries long-running provider orchestration work.
- Azure Blob Storage stores provider outputs and temporary downloadable assets.
- Azure Table Storage stores durable generation job state and App Attest challenge/session state.
- Azure SQL stores account, auth, purchase, credit, ledger, and generation ownership state.
- Azure App Configuration stores provider/model routing settings and feature flags.
- Azure Key Vault holds fal.ai, Luma, and other external provider credentials.
- Managed identity limits secret exposure and grants scoped Azure resource access.

The current local backend keeps an in-memory job store, in-memory App Attest state store, in-memory account store, and fake provider for deterministic development. Deployed environments use Azure SQL, Azure Table Storage, Queue Storage, Blob Storage, managed identity, App Configuration, Key Vault, Sign in with Apple verification, StoreKit JWS verification, and App Attest enforcement without changing the iOS-facing generation contract. App Attest challenge and session state are shared through Azure Table Storage in deployed environments so scaled API replicas do not lose valid clients. After validation, moderation, and provider submission, persisted generation job state clears raw `originalPrompt`, visible caption text, source-media bytes, and processed source-image bytes while keeping metadata needed for worker processing and client-mediated retry. Generation jobs include `expiresAt`; deployed defaults expire remaining job metadata and result links after 24 hours, prune expired table rows in cleanup passes, release expired credit reservations, and delete temporary provider result blobs through a 2-day Azure Storage lifecycle policy. Generation lifecycle logs are metadata-only and omit prompt text, caption text, media bytes, result bytes, provider error messages, bearer tokens, App Attest sessions, Apple identity tokens, and StoreKit signed payloads. The App Attest service fails closed unless either the explicit demo bypass is enabled for local testing or a real verifier is configured with the app identifier and Apple App Attest root certificate.

The subscription-scoped Bicep template in `infra/main.subscription.bicep` bootstraps environment resource groups, such as `rg-gifforge-nonprod` and `rg-gifforge-prod`, and deploys `infra/main.bicep` into them. Repeated environment updates should use resource-group-scope deployments of `infra/main.bicep`; the manual deploy workflows follow that model so their Azure OIDC identities can be scoped to environment resource groups instead of the whole subscription. `scripts/setup-azure-oidc.sh` is the dry-run-first setup path for per-environment GitHub OIDC trust, GitHub environment secrets, and resource-group-scoped RBAC. GifForge has two planned environments: `nonprod` and `prod`. The templates intentionally avoid provider-specific assumptions; provider credentials should be supplied as Container Apps secrets or inserted into Key Vault out-of-band for provider-specific adapter work.

## iMessage Extension Behavior

The extension treats Messages as a short-lived surface:

- Backend owns long-running job state.
- The extension stores recent local output and reloads history when reopened.
- The extension persists active generation snapshots in the shared app container so a reopened extension resumes polling instead of creating duplicate jobs.
- Active generation snapshots also retain source media locally for user-confirmed retry; the app clears that material on success, backend/job expiry, local cleanup, or when the user declines retry.
- The containing app can clear completed history and resumable active-job metadata from the shared container.

Messages insertion is limited to `MSConversation.insertAttachment`. GifForge does not auto-send. Sticker APIs are not used in v1.

## GIF Generation Pipeline

1. User input becomes `GenerationIntent`.
2. Prompt planning produces `StructuredGenerationRequest`.
3. Backend job returns a generated MP4 or frame-sequence motion asset from a stable GifForge URL.
4. The app turns the motion asset into frames.
5. The app renders visible caption text onto frames locally.
6. ImageIO writes an animated GIF.
7. The extension previews the file and inserts it into Messages as an attachment.

The fake provider returns `frame-sequence-v1` so the pipeline is testable without video decoding. Real AI providers should return short silent MP4s, preferably 3-5 seconds. MP4 results are supported through AVFoundation frame extraction before the same caption and GIF rendering steps. AI providers do not generate the final GIF; the iOS client converts downloaded MP4 videos into GIFs locally. The GIF renderer caps frame count, dimensions, and final file size for Messages-oriented output.

## Remaining MVP Work

- Add UI flows for picking GIF, MP4, MOV, and Live Photo paired MOV assets from Photos and passing them as `SourceMedia`.
- Validate direct fal.ai and Luma request/response templates against live provider accounts and adjust provider-specific JSON fields if their APIs require signed upload URLs instead of data URLs.
- Validate the generic provider callback route against the selected production provider or gateway. Queue polling remains the compatibility path.
