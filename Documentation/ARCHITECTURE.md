# Architecture Overview

Gifster is split into five bounded areas:

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

The backend returns a job id and status URL. The app polls `GET /v1/generations/:jobId`. On success the backend returns a temporary `downloadUrl`. The demo provider serves a frame sequence JSON result; real adapters can serve MP4 or frame-sequence assets under the same app-facing contract.

The backend can run with `GIFSTER_PROVIDER_ADAPTER=fake` for deterministic development or `GIFSTER_PROVIDER_ADAPTER=external-http` for a provider-neutral HTTP adapter. The external adapter is intended for either a compatible provider gateway or a vendor-specific wrapper service owned by the backend team, so Gifster remains replaceable-provider-first and does not assume a specific AI media vendor.

## Backend Runtime

Gifster targets ASP.NET Core Minimal API with Native AOT on Azure Container Apps for production. The API process should stay stateless and low-memory so the consumption workload profile can scale to zero and scale out efficiently.

Production Azure services should be split by responsibility:

- Azure Container Apps hosts the public Minimal API and a separate queue worker app from the same container image.
- Azure Queue Storage carries long-running provider orchestration work.
- Azure Blob Storage stores provider outputs and temporary downloadable assets.
- Azure Table Storage stores durable generation job state.
- Azure Key Vault or Container Apps secrets hold external provider credentials.
- Managed identity limits secret exposure and grants scoped Azure resource access.

The current local backend keeps an in-memory job store and fake provider for deterministic development. Deployed environments use Azure Table Storage, Queue Storage, Blob Storage, managed identity, and App Attest enforcement without changing the iOS-facing generation contract. The App Attest service fails closed unless either the explicit demo bypass is enabled for local/nonprod testing or a real verifier is configured with the app identifier and Apple App Attest root certificate.

The subscription-scoped Bicep template in `infra/main.subscription.bicep` creates the environment resource group, such as `rg-gifster-nonprod`, and deploys `infra/main.bicep` into it. Gifster has two planned environments: `nonprod` and `prod`. The templates intentionally do not create provider API secrets; those should be inserted into Key Vault out-of-band and accessed by the backend through managed identity.

## iMessage Extension Behavior

The extension treats Messages as a short-lived surface:

- Backend owns long-running job state.
- The extension stores recent local output and reloads history when reopened.
- The extension persists active generation snapshots in the shared app container so a reopened extension resumes polling instead of creating duplicate jobs.
- The containing app can clear completed history and resumable active-job metadata from the shared container.

Messages insertion is limited to `MSConversation.insertAttachment`. Gifster does not auto-send. Sticker APIs are not used in v1.

## GIF Generation Pipeline

1. User input becomes `GenerationIntent`.
2. Prompt planning produces `StructuredGenerationRequest`.
3. Backend job returns a generated motion asset.
4. The app turns the motion asset into frames.
5. The app renders visible caption text onto frames locally.
6. ImageIO writes an animated GIF.
7. The extension previews the file and inserts it into Messages as an attachment.

The fake provider returns `frame-sequence-v1` so the pipeline is testable without video decoding. MP4 results are supported through AVFoundation frame extraction before the same caption and GIF rendering steps. The GIF renderer caps frame count, dimensions, and final file size for Messages-oriented output.
