# Backend API

## POST `/v1/generations`

Creates a provider-neutral generation job.

```json
{
  "mode": "text_to_gif",
  "cleanedPrompt": "a corgi typing at a tiny laptop",
  "expandedPrompt": "Create a looping animated GIF of a corgi typing at a tiny laptop with a clear subject, short seamless motion, readable composition, no embedded text, and no watermarks.",
  "negativePrompt": "readable text, captions, subtitles, logos, watermarks, violent or sexual content",
  "caption": {
    "mode": "user_text",
    "text": "ship it",
    "renderLocally": true
  },
  "options": {
    "width": 480,
    "height": 360,
    "loopSeconds": 2,
    "stylePreset": "expressive-loop",
    "motionIntensity": "medium"
  },
  "sourceImage": null,
  "clientFeatures": [
    "local_caption_rendering",
    "attachment_insertion"
  ]
}
```

Response:

```json
{
  "jobId": "uuid",
  "statusUrl": "http://127.0.0.1:8787/v1/generations/uuid"
}
```

## GET `/v1/generations/:jobId`

Returns job state.

```json
{
  "jobId": "uuid",
  "status": "succeeded",
  "progress": 1,
  "message": "Generated",
  "downloadUrl": "http://127.0.0.1:8787/v1/generations/uuid/result",
  "provider": "fake-frame-sequence"
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

Real provider adapters can map to MP4 or frame sequence assets while keeping this app-facing job lifecycle stable.
