# Privacy and Safety Notes

## Data Minimization

- Gifster uses only images selected by the user.
- The app uses a scoped image picker and does not request broad photo library access for v1.
- Selected images are downscaled and rewritten as JPEG before upload, stripping metadata in the process.
- Generated GIFs and local history are stored only in the shared app container and can be cleared from the containing app.
- The app and Messages extension include `PrivacyInfo.xcprivacy` manifests declaring no tracking and app-functionality use of prompts, selected images, generated content metadata, and UserDefaults.

## User Disclosure

The containing app discloses that prompts and selected images may be sent through the Gifster backend to external AI media providers. It also discloses that prompt planning and caption suggestions use local Apple models where available.

The checked-in privacy manifests support App Store privacy review, and `Documentation/PRIVACY_POLICY.md` contains the public privacy policy draft. App Store Connect privacy answers and the published privacy policy URL still need to match the deployed backend retention and provider-sharing behavior.

## Backend Responsibilities

The backend must:

- Validate request shape and payload size.
- Apply moderation and safety checks before provider submission.
- Hide external provider credentials.
- Translate app-level requests into provider-specific requests.
- Track long-running jobs.
- Return only temporary result URLs.
- Support deletion and retention policies for generated intermediate assets.

## Caption Safety

Visible caption text is rendered locally into the final GIF. The provider is asked not to render readable text, captions, logos, or watermarks into the generated motion result. This keeps caption edits fast and avoids provider-specific text rendering failures.

## Retention Expectations

The app should keep local GIF history only while useful to the user. The backend should expire provider result URLs quickly and should delete provider intermediate media according to a documented retention window. A production backend should expose authenticated deletion endpoints if it stores user-linked jobs.
