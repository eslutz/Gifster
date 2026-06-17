# Backend API

## GET `/health`

Returns backend health and active provider mode for Container Apps probes.

```json
{
  "ok": true,
  "provider": "fake-frame-sequence",
  "mode": "demo"
}
```

Deployed environments use the direct video router and report `provider=routed-video`, `mode=video`. Tests may inject the fake provider, which reports `mode=demo`.

## POST `/v1/app-attest/challenges`

Creates a short-lived App Attest challenge. Local demo mode can use this endpoint with `GIFFORGE_APP_ATTEST_DEMO_BYPASS=true`; production uses it as the challenge source for server-side App Attest verification.

```json
{
  "challengeId": "uuid",
  "challenge": "base64url-random",
  "expiresAt": "2026-06-14T23:00:00Z"
}
```

## POST `/v1/app-attest/attestations`

Exchanges an App Attest response for a short-lived App Attest session token. Generation, status, and result routes require this token in `X-GifForge-App-Attest-Session` when `GIFFORGE_APP_ATTEST_REQUIRED=true`.

The backend rejects placeholder attestation material unless `GIFFORGE_APP_ATTEST_DEMO_BYPASS=true` is explicitly configured. With `GIFFORGE_APP_ATTEST_APP_IDENTIFIER` and `GIFFORGE_APP_ATTEST_ROOT_CERTIFICATE_PEM` configured, it verifies challenge binding, attestation CBOR, certificate trust, nonce binding, key-id matching, RP/app-id hash, and COSE public key matching before issuing a session token.

```json
{
  "keyId": "app-attest-key-id",
  "challengeId": "uuid",
  "attestationObject": "base64-attestation-object",
  "clientDataHash": "base64-client-data-hash"
}
```

Response:

```json
{
  "sessionToken": "base64url-session-token",
  "expiresAt": "2026-06-15T07:00:00Z"
}
```

## POST `/v1/auth/apple`

Exchanges a Sign in with Apple identity token for a GifForge backend session. The backend verifies Apple JWT signature, issuer, audience, expiration, subject, and nonce before creating or updating the SQL-backed user record.

```json
{
  "identityToken": "apple-jwt",
  "nonce": "raw-client-nonce"
}
```

Response:

```json
{
  "userId": "uuid",
  "appAccountToken": "uuid",
  "accessToken": "backend-access-token",
  "accessTokenExpiresAt": "2026-06-16T23:30:00Z",
  "refreshToken": "opaque-refresh-token",
  "refreshTokenExpiresAt": "2026-07-16T23:15:00Z"
}
```

Store backend tokens in the shared Keychain only. The containing app owns Sign in with Apple and purchase UI; the Messages extension reads the shared Keychain state.

## POST `/v1/auth/refresh`

Rotates a refresh token and returns a new backend session. Refresh tokens are stored server-side as hashes. Reusing a previously rotated token revokes that refresh-token family.

```json
{
  "refreshToken": "opaque-refresh-token"
}
```

## POST `/v1/auth/logout`

Revokes the submitted refresh token.

```json
{
  "refreshToken": "opaque-refresh-token"
}
```

## GET `/v1/me`

Requires:

```http
Authorization: Bearer <backend-access-token>
```

Returns the SQL-backed GifForge user id and Apple `appAccountToken` used for StoreKit purchases.

## GET `/v1/me/credits`

Requires backend bearer auth and returns granted, captured, reserved, and available credits.

```json
{
  "grantedCredits": 10,
  "capturedDebits": 1,
  "reservedCredits": 0,
  "availableCredits": 9
}
```

## GET `/v1/iap/products`

Requires backend bearer auth. Returns active Apple consumable product ids and credit amounts. Prices are not billing truth in the backend; App Store Connect owns pricing.

Initial product ids:

- `dev.ericslutz.gifforge.credits.10`
- `dev.ericslutz.gifforge.credits.25`
- `dev.ericslutz.gifforge.credits.60`

## POST `/v1/iap/transactions`

Requires backend bearer auth. The client submits Apple StoreKit's signed transaction JWS after StoreKit reports a verified purchase. The backend verifies the JWS signature/certificate chain against the configured Apple root certificate, bundle id, product id, app account token, transaction type, and revocation state before granting credits. Empty Apple root configuration fails closed. Consumable transactions are idempotent by Apple transaction id. The client finishes the StoreKit transaction only after this endpoint confirms the grant.

```json
{
  "productId": "dev.ericslutz.gifforge.credits.10",
  "signedTransaction": "storekit-jws"
}
```

## POST `/v1/apple/app-store-server-notifications`

Accepts App Store Server Notifications v2 signed payloads. Refund/revoke notification JWS payloads are verified against the configured Apple root certificate before the nested transaction id is reversed in the credit ledger. Empty Apple root configuration fails closed. If already-spent credits make the user balance negative, new generation reservations are blocked until the account returns to a non-negative available balance.

## POST `/v1/generations`

Creates a provider-neutral generation job.

When backend auth and App Attest are required, include both headers:

```http
Authorization: Bearer <backend-access-token>
X-GifForge-App-Attest-Session: <app-attest-session-token>
```

On accepted generation requests, SQL reserves one credit before the job is queued. A reservation reduces available balance so a user cannot start more concurrent jobs than they can afford. The worker captures the reservation only after a usable provider result is stored; provider/backend terminal failure or expiry releases the reservation.

```json
{
  "id": "96DD3998-C2E1-4C39-B7B1-3559D0D271C8",
  "mode": "text_to_gif",
  "originalPrompt": "a corgi typing at a tiny laptop",
  "cleanedPrompt": "a corgi typing at a tiny laptop",
  "expandedPrompt": "Create a looping animated GIF of a corgi typing at a tiny laptop with a clear subject, short seamless motion, readable composition, no embedded text, and no watermarks.",
  "negativePrompt": "readable text, captions, subtitles, logos, watermarks, violent or sexual content",
  "caption": {
    "mode": "userText",
    "text": "ship it"
  },
  "sourceMedia": null,
  "sourceImage": null,
  "sourceImageContext": null,
  "options": {
    "width": 480,
    "height": 360,
    "loopSeconds": 2,
    "stylePreset": "expressive-loop",
    "motionIntensity": "medium"
  },
  "clientTraceId": "trace-id",
  "retryOfJobId": null
}
```

Validation rules:

- `mode` must be `text_to_gif`, `image_to_gif`, or `video_to_gif`.
- `cleanedPrompt` is required and limited to 600 characters.
- `expandedPrompt` is limited to 1,600 characters.
- Caption mode must be `none`, `userText`, or `suggestWithAI`; caption text is limited to 64 characters.
- `options.width` and `options.height`, when present, must be between 64 and 1,024 pixels.
- `options.loopSeconds`, when present, must be between 0.5 and 6.0 seconds.
- `options.motionIntensity`, when present, must be `subtle`, `medium`, or `high`.
- `image_to_gif` requests must include either `sourceImage` or `sourceMedia`.
- `sourceImage` must be the app-processed, metadata-stripped JPEG payload (`image/jpeg`), valid base64, no larger than the processed upload limit, and no wider or taller than 1,024 pixels.
- `sourceImageContext`, when present, is metadata-only local context such as dimensions, orientation, aspect ratio, and a short summary. Its dimensions must match `sourceImage`.
- `sourceMedia`, when present, supports JPEG, PNG, HEIC/HEIF, GIF, MP4, MOV, and Live Photo paired MOV uploads. GIF, MP4, MOV, and Live Photo paired MOV route to video-to-video provider models. JPEG, PNG, and HEIC/HEIF route to image-to-video provider models.
- `video_to_gif` requests must include `sourceMedia`.
- Live Photo requests must send the paired MOV as `sourceMedia` with `mimeType=video/quicktime` and `role=livePhotoPairedVideo`; sending only the still image is rejected for the Live Photo workflow.
- `retryOfJobId`, when present, must reference a failed generation job whose attempt count has not reached `GIFFORGE_GENERATION_MAX_ATTEMPTS`. The backend uses the previous job's attempted provider/model metadata to select the next cheapest compatible candidate.

Response:

```json
{
  "jobId": "uuid",
  "status": "queued",
  "statusUrl": "http://127.0.0.1:8787/v1/generations/uuid",
  "expiresAt": "2026-06-16T12:00:00Z"
}
```

## GET `/v1/generations/:jobId`

Returns job state.

```json
{
  "jobId": "uuid",
  "status": "succeeded",
  "downloadUrl": "http://127.0.0.1:8787/v1/generations/uuid/result",
  "message": null,
  "expiresAt": "2026-06-16T12:00:00Z",
  "retryAvailable": false,
  "retryReason": null,
  "retryOfJobId": null
}
```

Failed jobs can return `"retryAvailable": true`, `"retryReason": "provider_failed"`, and `"retryOfJobId": "<failed-job-id>"`. Expired jobs return HTTP `410 Gone` from both the status and result routes. Protected status and result reads check SQL generation ownership before reading Table Storage job state. After validation, moderation, and provider submission, persisted job state clears raw `originalPrompt`, visible caption text, source-media bytes, and processed source-image bytes. Deployed environments default to expiring remaining job metadata and result links after 24 hours.

## GET `/v1/generations/:jobId/result`

Returns a temporary generated motion asset. Real provider-backed jobs return MP4 assets from GifForge-controlled storage. Tests may inject the demo provider, which returns a frame sequence JSON payload:

```json
{
  "format": "frame-sequence-v1",
  "width": 480,
  "height": 360,
  "promptEcho": "a corgi typing at a tiny laptop",
  "frames": [
    {
      "index": 0,
      "duration": 0.08,
      "backgroundHex": "0B132B",
      "accentHex": "5BC0BE",
      "motionOffset": 0
    }
  ]
}
```

The iOS app converts downloaded MP4 videos into frames, renders captions locally, and writes the final GIF before Messages insertion.

## Direct Video Provider Router

The backend always starts the direct provider router. The router classifies each request:

- prompt only -> text-to-video
- still image -> image-to-video
- GIF, MP4, MOV, or Live Photo paired MOV -> video-to-video

Enabled provider/model candidates are ordered by effective estimated cost. Provider/model identity lives in the backend C# model catalog; App Configuration can enable providers and override model costs, but it must not define provider model IDs. When an enabled flag is omitted, a provider is enabled only if its API key is configured; explicitly enabling a provider without its API key fails startup. fal.ai Wan 2.2 defaults are cheaper and selected first. Luma Ray 3.2 defaults are configured as retry candidates. The backend submits to the cheapest compatible candidate for the request. If the provider fails, the job response can include retry metadata; the iOS client keeps the original request media locally, asks the user, and only resubmits with `retryOfJobId` after confirmation. The retry submission skips providers/models already attempted for the previous job.

Provider configuration should come from Azure App Configuration, with API keys stored in Azure Key Vault and referenced by App Configuration or loaded directly from Key Vault. Supported settings include:

- `GIFFORGE_FAL_ENABLED`
- `GIFFORGE_FAL_API_KEY`
- `GIFFORGE_FAL_SUBMIT_URL_TEMPLATE`
- `GIFFORGE_FAL_RESULT_URL_TEMPLATE`
- `GIFFORGE_MODEL_COST_USD_FAL_WAN22_TEXT_TO_VIDEO`
- `GIFFORGE_MODEL_COST_USD_FAL_WAN22_IMAGE_TO_VIDEO`
- `GIFFORGE_MODEL_COST_USD_FAL_WAN22_VIDEO_TO_VIDEO`
- `GIFFORGE_LUMA_ENABLED`
- `GIFFORGE_LUMA_API_KEY`
- `GIFFORGE_LUMA_SUBMIT_URL_TEMPLATE`
- `GIFFORGE_LUMA_RESULT_URL_TEMPLATE`
- `GIFFORGE_MODEL_COST_USD_LUMA_RAY32_TEXT_TO_VIDEO`
- `GIFFORGE_MODEL_COST_USD_LUMA_RAY32_IMAGE_TO_VIDEO`
- `GIFFORGE_MODEL_COST_USD_LUMA_RAY32_VIDEO_TO_VIDEO`

The backend downloads provider result assets and stores generated MP4s in GifForge-controlled storage. The app receives only `/v1/generations/:jobId/result` URLs, never provider URLs.

The backend does not persist source media for retry/fallback. After validation and provider submission, durable job state clears raw source-media and source-image bytes. Retry material remains on the client device in the active-generation snapshot until the job succeeds, expires, or the user declines retry. `GIFFORGE_GENERATION_MAX_ATTEMPTS` caps total client-mediated retry attempts, defaulting to 3.

Failed job status responses include `retryAvailable`, `retryReason`, and `retryOfJobId` when the backend can accept a user-confirmed retry.

## POST `/v1/provider-callbacks/:jobId`

Provider gateways can push completion to this endpoint instead of waiting for queue polling. If `GIFFORGE_PROVIDER_CALLBACK_SECRET` is configured, callbacks must include:

```http
X-GifForge-Provider-Callback-Secret: <secret>
```

Successful callback:

```json
{
  "status": "succeeded",
  "providerJobId": "provider-job-id",
  "assetUrl": "https://provider.example/video.mp4",
  "contentType": "video/mp4"
}
```

The backend validates the provider job id, downloads the asset, stores it in GifForge-controlled result storage, marks the job succeeded, and keeps returning the stable `/v1/generations/:jobId/result` URL to the iOS client. Failed callbacks mark the job failed with generic app-safe copy and retry metadata when another configured provider/model remains available. Unknown in-progress statuses are accepted without changing the job.

Use `Documentation/PROVIDER_ONBOARDING.md` and `scripts/validate-provider-onboarding.rb` to record the provider decision, text/image/video preflight evidence, privacy/security review, cost/rate-limit review, and production secret plan before enabling paid providers in production.
