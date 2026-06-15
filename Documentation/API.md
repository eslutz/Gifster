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

## POST `/v1/app-attest/challenges`

Creates a short-lived App Attest challenge. Local demo mode can use this endpoint with `GIFSTER_APP_ATTEST_DEMO_BYPASS=true`; production uses it as the challenge source for server-side App Attest verification.

```json
{
  "challengeId": "uuid",
  "challenge": "base64url-random",
  "expiresAt": "2026-06-14T23:00:00Z"
}
```

## POST `/v1/app-attest/attestations`

Exchanges an App Attest response for a short-lived backend session token. Generation, status, and result routes require this token when `GIFSTER_APP_ATTEST_REQUIRED=true`.

The backend rejects placeholder attestation material unless `GIFSTER_APP_ATTEST_DEMO_BYPASS=true` is explicitly configured. With `GIFSTER_APP_ATTEST_APP_IDENTIFIER` and `GIFSTER_APP_ATTEST_ROOT_CERTIFICATE_PEM` configured, it verifies challenge binding, attestation CBOR, certificate trust, nonce binding, key-id matching, RP/app-id hash, and COSE public key matching before issuing a session token.

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

## POST `/v1/generations`

Creates a provider-neutral generation job.

When App Attest is required, include:

```http
Authorization: Bearer <sessionToken>
```

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
  "sourceImage": null,
  "options": {
    "width": 480,
    "height": 360,
    "loopSeconds": 2,
    "stylePreset": "expressive-loop",
    "motionIntensity": "medium"
  },
  "clientTraceId": "trace-id"
}
```

Response:

```json
{
  "jobId": "uuid",
  "status": "queued",
  "statusUrl": "http://127.0.0.1:8787/v1/generations/uuid"
}
```

## GET `/v1/generations/:jobId`

Returns job state.

```json
{
  "jobId": "uuid",
  "status": "succeeded",
  "downloadUrl": "http://127.0.0.1:8787/v1/generations/uuid/result",
  "message": null
}
```

## GET `/v1/generations/:jobId/result`

Returns a temporary generated motion asset. The demo provider returns a frame sequence JSON payload:

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

Real provider adapters can return either a frame sequence JSON payload or a direct `video/mp4` payload from the result URL. The iOS app converts either result type into frames, renders captions locally, and writes the final GIF before Messages insertion.

## External HTTP Provider Contract

Set `GIFSTER_PROVIDER_ADAPTER=external-http` to use the provider-neutral HTTP adapter. The backend posts the same `GenerationRequest` JSON used by `/v1/generations` to `GIFSTER_EXTERNAL_PROVIDER_SUBMIT_URL`.

Submission response:

```json
{
  "providerJobId": "provider-specific-job-id"
}
```

The backend later downloads the motion asset from `GIFSTER_EXTERNAL_PROVIDER_RESULT_URL_TEMPLATE`, replacing `{providerJobId}` and `{jobId}` placeholders. The result response must use either:

- `application/vnd.gifster.frame-sequence+json`
- `video/mp4`
