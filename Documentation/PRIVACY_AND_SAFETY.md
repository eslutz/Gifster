# Privacy and Safety Notes

## Data Minimization

- Gifster uses only images selected by the user.
- The app uses a scoped image picker and does not request broad photo library access for v1.
- Selected images are downscaled and rewritten as JPEG before upload, stripping metadata in the process.
- Generated GIFs and local history are stored only in the shared app container and can be cleared from the containing app.
- The app and Messages extension include `PrivacyInfo.xcprivacy` manifests declaring no tracking and app-functionality use of prompts, selected images, generated content metadata, and UserDefaults.

## User Disclosure

The containing app discloses that prompts and selected images may be sent through the Gifster backend to external AI media providers. It also discloses that prompt planning and caption suggestions use local Apple models where available.

The checked-in privacy manifests support App Store privacy review, and `Documentation/PRIVACY_POLICY.md` contains the public privacy policy draft. App Store metadata currently points to the public GitHub copy of that policy as a fallback. App Store Connect privacy answers and the final public privacy policy URL still need to match the deployed backend retention and provider-sharing behavior.

## Backend Responsibilities

The backend must:

- Validate request shape and payload size.
- Accept only app-processed JPEG source images, valid base64, bounded source-image dimensions, bounded output dimensions, supported caption modes, and supported motion-intensity values.
- Apply moderation and safety checks before provider submission.
- Hide external provider credentials.
- Translate app-level requests into provider-specific requests.
- Track long-running jobs.
- Emit metadata-only operational logs for job lifecycle events without prompt text, caption text, source-image bytes, provider result bytes, or provider error messages.
- Return only temporary result URLs.
- Enforce retention for generated job metadata and intermediate assets.

## Caption Safety

Visible caption text is rendered locally into the final GIF. The provider is asked not to render readable text, captions, logos, or watermarks into the generated motion result. This keeps caption edits fast and avoids provider-specific text rendering failures.

## Retention Expectations

The app keeps local GIF history only while useful to the user. The backend now stores an expiration timestamp with each generation job and returns `410 Gone` after that point instead of exposing status or result links. Deployed defaults expire job rows after 24 hours and delete temporary provider/source blobs after 2 days through Azure Storage lifecycle policy. A future authenticated deletion endpoint is still appropriate if the product adds user accounts or user-linked backend history.
