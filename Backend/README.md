# GifForge Backend

GifForge uses an ASP.NET Core Minimal API backend configured for Native AOT and intended for Azure Container Apps.

The backend is provider-neutral. The app never calls external AI media providers directly; it submits structured generation requests to this service, which validates and moderates requests, owns provider credentials, tracks jobs, and returns temporary result URLs.

Sign in with Apple is the only account sign-in method, and Apple In-App Purchase consumable credit packs are the only payment provider. Azure SQL stores users, refresh tokens, IAP products/transactions, credit reservations, immutable ledger entries, generation ownership, and audit records. Azure Table Storage remains for operational generation jobs and short-lived App Attest state.

Request validation rejects unsupported modes, overlong prompts/captions, unsupported caption modes, out-of-range output options, non-JPEG source images, invalid base64 source data, oversized processed images, source-image dimensions larger than the app preprocessing limit, and mismatched source-image context metadata.

Operational generation logs are metadata-only. They include event name, job id, provider, mode, status, source-image presence, caption mode, result content type, and failure kind. They intentionally do not include prompt text, caption text, source-image bytes, provider result bytes, or provider error messages.

After validation, moderation, and provider submission, persisted generation job state is minimized. The backend clears raw `originalPrompt`, visible caption text, and processed source-image bytes before storing the job request while keeping the structured prompt, caption mode, source-image context, and options needed by the worker.

## Local Development

```bash
dotnet run --project Backend/GifForge.Backend.csproj
```

The backend listens on `http://127.0.0.1:8787` by default when launched directly.

## Tests

```bash
dotnet test Backend.Tests/GifForge.Backend.Tests.csproj
```

The xUnit test suite verifies the HTTP contract, App Attest authorization gates, shared App Attest state storage, explicit demo App Attest bypass behavior, demo provider, durable job mapping, retention expiry behavior, sanitized operational generation events, queue worker retry behavior, fake frame-sequence output, and moderation rejection.

## App Attest Modes

`GIFFORGE_APP_ATTEST_REQUIRED=true` requires generation, status, and result requests to include an App Attest session token in `X-GifForge-App-Attest-Session`. When `GIFFORGE_APP_ATTEST_APP_IDENTIFIER` and `GIFFORGE_APP_ATTEST_ROOT_CERTIFICATE_PEM` are configured, the backend verifies App Attest challenge binding, attestation CBOR, certificate trust, nonce binding, key-id matching, RP/app-id hash, and COSE public key matching before issuing that session token. `GIFFORGE_APP_ATTEST_APP_IDENTIFIER` accepts a comma-separated allowlist for the containing app and Messages extension identifiers.

Local development uses an in-memory App Attest state store. Storage-configured deployments use Azure Table Storage for challenge and session state so challenge exchange and authorized generation requests keep working across multiple API replicas and restarts.

The demo bypass only issues session tokens when `GIFFORGE_APP_ATTEST_DEMO_BYPASS=true`. Do not set `GIFFORGE_APP_ATTEST_DEMO_BYPASS` in deployed nonprod or production environments.

## Auth, Credits, and IAP

`GIFFORGE_AUTH_REQUIRED=true` requires protected APIs to include `Authorization: Bearer <backend-access-token>`. The backend verifies Apple identity tokens against Apple's JWKS, expected issuer, configured audience, expiration, and nonce. Access tokens are short-lived backend tokens; refresh tokens are opaque, hashed in SQL, rotated on use, and family-revoked on reuse detection.

StoreKit consumable products are seeded in SQL:

- `dev.ericslutz.gifforge.credits.10`
- `dev.ericslutz.gifforge.credits.25`
- `dev.ericslutz.gifforge.credits.60`

The backend grants credits only after verifying StoreKit transaction JWS signature/certificate chain, bundle id, product id, transaction type, app account token, and revocation state. `GIFFORGE_APP_STORE_JWS_ROOT_CERTIFICATE_PEM` must contain the Apple root certificate used for StoreKit/App Store Server Notification JWS chains when the demo IAP bypass is disabled; an empty value fails closed instead of falling back to the OS trust store. Signed transaction payloads are not stored raw; SQL stores a payload hash and immutable ledger entries. App Store Server Notifications v2 refund/revoke payloads are verified before inserting reversal entries.

Generation requests use reserve-then-capture accounting. SQL reserves one credit before a job is queued, reducing available balance during concurrent work. The worker captures the reservation after a usable provider result is stored. Terminal failure and expiry release the reservation.

Production requires a separate production SQL database before live users or live purchases. Nonprod can use `ericslutz-dev-db.database.windows.net` / `ericslutz.dev.db` with schema `gifforge`.

## Retention

Generation jobs include an `expiresAt` timestamp. After expiry, status and result routes return HTTP `410 Gone` so remaining job metadata and result links are no longer exposed through the app-facing API.

Runtime settings:

- `GIFFORGE_GENERATION_JOB_RETENTION_HOURS`: job metadata and result-link lifetime. Default: `24`.
- `GIFFORGE_RETENTION_CLEANUP_ENABLED`: enables background deletion of expired job rows. Bicep deployments set this to `true`.
- `GIFFORGE_RETENTION_CLEANUP_INTERVAL_MINUTES`: cleanup interval. Default deployment value: `360`.
- `GIFFORGE_RETENTION_CLEANUP_BATCH_SIZE`: maximum expired job rows removed per cleanup pass. Default deployment value: `100`.

Azure deployments also configure Storage lifecycle deletion for temporary provider result blobs. The default Bicep value is `temporaryBlobRetentionDays=2`. Source media used for retry remains on the client device and is not retained in backend blob storage.

## Azure Container Apps Direction

Production should run this API as a small containerized Minimal API on Azure Container Apps using a consumption workload profile.

Recommended supporting services:

- Azure Queue Storage for asynchronous provider orchestration.
- Azure Blob Storage for provider output and temporary downloadable media with lifecycle deletion.
- Azure Table Storage for durable job state and App Attest challenge/session state.
- Azure SQL for users, backend sessions, Apple IAP, credits, ledger, and generation ownership.
- Azure Key Vault or Container Apps secrets for provider credentials.
- Managed identity for Azure resource access.
- Application Insights for logs, metrics, and request tracing.

Native AOT is enabled in `GifForge.Backend.csproj` to reduce cold-start overhead and memory usage compared with a standard JIT ASP.NET Core deployment.

Linux Native AOT publishing requires native linker dependencies. The Dockerfile and backend workflow install `clang`, `libicu-dev`, `libssl-dev`, and `zlib1g-dev` before `dotnet publish` so HTTPS, globalization, compression, and native linking succeed in the container build. The runtime image also installs ICU and the backend publishes with full globalization support because `Microsoft.Data.SqlClient` requires culture data when opening SQL connections.

## Direct Video Providers

The runtime backend always starts with the direct video provider router. The router builds enabled providers from current configuration, classifies each request as text-to-video, image-to-video, or video-to-video, and submits to the cheapest compatible C# model catalog entry.

Provider/model identity and capabilities are defined in backend code. App Configuration must define every enabled model cost; the backend does not carry provider pricing defaults. Store `GIFFORGE_FAL_API_KEY` and `GIFFORGE_LUMA_API_KEY` in Azure Key Vault and expose them through App Configuration Key Vault references. When an enabled flag is omitted, a provider is enabled only if its API key is configured. If a provider is explicitly enabled without its API key, or if any enabled provider model cost is missing or invalid, startup fails closed with a clear configuration error.

The backend registers all loaded App Configuration keys for refresh. HTTP requests trigger request-driven refresh through Azure App Configuration middleware, and the queue worker triggers refresh before processing each dequeued generation job. Provider routing is rebuilt from current configuration for each provider operation so App Configuration price changes are picked up after refresh without changing backend code.

Supported operational settings:

- `GIFFORGE_FAL_ENABLED`, `GIFFORGE_LUMA_ENABLED`
- `GIFFORGE_FAL_SUBMIT_URL_TEMPLATE`, `GIFFORGE_FAL_RESULT_URL_TEMPLATE`
- `GIFFORGE_LUMA_SUBMIT_URL_TEMPLATE`, `GIFFORGE_LUMA_RESULT_URL_TEMPLATE`
- `GIFFORGE_MODEL_COST_USD_FAL_WAN22_TEXT_TO_VIDEO`
- `GIFFORGE_MODEL_COST_USD_FAL_WAN22_IMAGE_TO_VIDEO`
- `GIFFORGE_MODEL_COST_USD_FAL_WAN22_VIDEO_TO_VIDEO`
- `GIFFORGE_MODEL_COST_USD_LUMA_RAY32_TEXT_TO_VIDEO`
- `GIFFORGE_MODEL_COST_USD_LUMA_RAY32_IMAGE_TO_VIDEO`
- `GIFFORGE_MODEL_COST_USD_LUMA_RAY32_VIDEO_TO_VIDEO`

The `/health` response reports `provider=routed-video` and `mode=video` for deployed provider-backed environments.

Provider submit responses of `400`, `401`, `403`, or `422` are treated as permanent provider rejections and return a generic app-facing HTTP `422` without persisting a job. Availability failures such as `408`, `429`, `5xx`, network errors, and timeout-style failures are treated as retryable provider outages and return a generic app-facing HTTP `503`. Provider error bodies are not exposed to the app.

The backend stores generated provider MP4 assets in application-controlled storage and returns stable GifForge result URLs. The iOS app still renders captions locally and creates the final GIF.

Before selecting the first paid provider, create and validate provider onboarding evidence:

```bash
scripts/validate-provider-onboarding.rb --template Documentation/ProviderEvidence/first-provider.json
scripts/validate-provider-onboarding.rb Documentation/ProviderEvidence/first-provider.json
```

The onboarding evidence covers text/image/video preflight results, accepted MP4 result handling, no provider-rendered captions, server-side-only credentials, data-use and retention review, cost/rate-limit review, outage fallback, and production rollback. Do not store credential values in the evidence file.
