# Gifster Backend

Gifster uses an ASP.NET Core Minimal API backend configured for Native AOT and intended for Azure Container Apps.

The backend is provider-neutral. The app never calls external AI media providers directly; it submits structured generation requests to this service, which validates and moderates requests, owns provider credentials, tracks jobs, and returns temporary result URLs.

Request validation rejects unsupported modes, overlong prompts/captions, unsupported caption modes, out-of-range output options, non-JPEG source images, invalid base64 source data, oversized processed images, source-image dimensions larger than the app preprocessing limit, and mismatched source-image context metadata.

Operational generation logs are metadata-only. They include event name, job id, provider, mode, status, source-image presence, caption mode, result content type, and failure kind. They intentionally do not include prompt text, caption text, source-image bytes, provider result bytes, or provider error messages.

After validation, moderation, and provider submission, persisted generation job state is minimized. The backend clears raw `originalPrompt`, visible caption text, and processed source-image bytes before storing the job request while keeping the structured prompt, caption mode, source-image context, and options needed by the worker.

## Local Development

```bash
dotnet run --project Backend/Gifster.Backend.csproj
```

The backend listens on `http://127.0.0.1:8787` by default when launched directly.

## Tests

```bash
dotnet test Backend.Tests/Gifster.Backend.Tests.csproj
```

The xUnit test suite verifies the HTTP contract, App Attest authorization gates, shared App Attest state storage, explicit demo App Attest bypass behavior, demo provider, durable job mapping, retention expiry behavior, sanitized operational generation events, queue worker retry behavior, fake frame-sequence output, and moderation rejection.

## App Attest Modes

`GIFSTER_APP_ATTEST_REQUIRED=true` requires generation, status, and result requests to include a backend session token. When `GIFSTER_APP_ATTEST_APP_IDENTIFIER` and `GIFSTER_APP_ATTEST_ROOT_CERTIFICATE_PEM` are configured, the backend verifies App Attest challenge binding, attestation CBOR, certificate trust, nonce binding, key-id matching, RP/app-id hash, and COSE public key matching before issuing that session token.

Local development uses an in-memory App Attest state store. Storage-configured deployments use Azure Table Storage for challenge and session state so challenge exchange and authorized generation requests keep working across multiple API replicas and restarts.

The demo bypass only issues session tokens when `GIFSTER_APP_ATTEST_DEMO_BYPASS=true`. Do not set `GIFSTER_APP_ATTEST_DEMO_BYPASS` in production.

## Retention

Generation jobs include an `expiresAt` timestamp. After expiry, status and result routes return HTTP `410 Gone` so remaining job metadata and result links are no longer exposed through the app-facing API.

Runtime settings:

- `GIFSTER_GENERATION_JOB_RETENTION_HOURS`: job metadata and result-link lifetime. Default: `24`.
- `GIFSTER_RETENTION_CLEANUP_ENABLED`: enables background deletion of expired job rows. Bicep deployments set this to `true`.
- `GIFSTER_RETENTION_CLEANUP_INTERVAL_MINUTES`: cleanup interval. Default deployment value: `360`.
- `GIFSTER_RETENTION_CLEANUP_BATCH_SIZE`: maximum expired job rows removed per cleanup pass. Default deployment value: `100`.

Azure deployments also configure Storage lifecycle deletion for temporary provider result and source-image blobs. The default Bicep value is `temporaryBlobRetentionDays=2`.

## Azure Container Apps Direction

Production should run this API as a small containerized Minimal API on Azure Container Apps using a consumption workload profile.

Recommended supporting services:

- Azure Queue Storage for asynchronous provider orchestration.
- Azure Blob Storage for provider output and temporary downloadable media with lifecycle deletion.
- Azure Table Storage for durable job state and App Attest challenge/session state.
- Azure Key Vault or Container Apps secrets for provider credentials.
- Managed identity for Azure resource access.
- Application Insights for logs, metrics, and request tracing.

Native AOT is enabled in `Gifster.Backend.csproj` to reduce cold-start overhead and memory usage compared with a standard JIT ASP.NET Core deployment.

Linux Native AOT publishing requires native linker dependencies. The Dockerfile and backend workflow install `clang`, `libssl-dev`, and `zlib1g-dev` before `dotnet publish` so HTTPS, compression, and native linking succeed in the container build.

## Provider Adapter Modes

`GIFSTER_PROVIDER_ADAPTER=fake` keeps the deterministic local/demo frame-sequence provider.

`GIFSTER_PROVIDER_ADAPTER=external-http` enables a provider-neutral HTTP adapter for a compatible provider gateway or vendor-specific wrapper service. It requires:

- `GIFSTER_EXTERNAL_PROVIDER_SUBMIT_URL`: receives a sanitized provider request JSON payload and returns `{ "providerJobId": "..." }`. Image-to-GIF requests include `sourceImage` plus optional `sourceImageContext` metadata derived locally by the app, such as dimensions, orientation, aspect ratio, and a short summary. The provider-facing payload omits `originalPrompt` and caption text, includes `captionMode`, and sets `renderCaptionLocally=true`.
- `GIFSTER_EXTERNAL_PROVIDER_RESULT_URL_TEMPLATE`: absolute URL template for downloading the provider result. Supports `{providerJobId}` and `{jobId}` placeholders.
- `GIFSTER_EXTERNAL_PROVIDER_AUTHORIZATION`: optional `Authorization` header value such as `Bearer <token>`.
- `GIFSTER_EXTERNAL_PROVIDER_NAME`: optional health/status display name.

Provider submit responses of `400`, `401`, `403`, or `422` are treated as permanent provider rejections and return a generic app-facing HTTP `422` without persisting a job. Availability failures such as `408`, `429`, `5xx`, network errors, and timeout-style failures are treated as retryable provider outages and return a generic app-facing HTTP `503`. Provider error bodies are not exposed to the app.

The result endpoint may return `application/vnd.gifster.frame-sequence+json` or `video/mp4`. Result `202` and `204` responses are treated as retryable not-ready states, while unsupported content types and empty motion assets are treated as permanent provider failures. The iOS app still renders captions locally and creates the final GIF.

Validate a compatible provider gateway before wiring it into production:

```bash
GIFSTER_EXTERNAL_PROVIDER_SUBMIT_URL=https://provider.example.test/jobs \
GIFSTER_EXTERNAL_PROVIDER_RESULT_URL_TEMPLATE='https://provider.example.test/jobs/{providerJobId}/result' \
GIFSTER_EXTERNAL_PROVIDER_AUTHORIZATION='Bearer <token>' \
scripts/validate-external-provider-contract.rb
```

Use `--mode image_to_gif` with `GIFSTER_PROVIDER_PRECHECK_IMAGE_BASE64`, `GIFSTER_PROVIDER_PRECHECK_IMAGE_WIDTH`, and `GIFSTER_PROVIDER_PRECHECK_IMAGE_HEIGHT` to validate the image-to-animation path.
