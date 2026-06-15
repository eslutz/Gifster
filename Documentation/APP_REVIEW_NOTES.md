# App Review Notes

Use this draft in App Store Connect review notes. Update environment details before submission, and replace GitHub fallback URLs with dedicated product-site URLs when available.

## Primary Flow

Gifster is an iMessage app extension. The primary experience is inside Messages:

1. Open Messages and select Gifster from the app drawer.
2. Enter a prompt describing the GIF.
3. Optionally select a user-chosen image.
4. Choose a caption mode: no caption, user text, or local AI-suggested text.
5. Generate and preview the GIF.
6. Tap Insert to place the GIF into the Messages compose field.
7. Send manually from Messages.

Gifster does not auto-send messages.

## Attachment Insertion Only

Gifster uses Messages attachment insertion for v1. It inserts the generated GIF into the Messages compose field as an attachment.

Sticker mode is not implemented in v1. Sticker APIs are not used in v1.

## AI and Backend Use

The app uses local Apple models where available for prompt cleanup, prompt expansion, request planning, and caption suggestions. If local Apple models are unavailable, the app uses deterministic local fallback planning.

Media generation requests are sent to the developer-operated backend. The backend validates requests, applies moderation and safety checks, hides external provider credentials, tracks long-running jobs, and returns temporary generated media results to the app.

The iOS app does not call external AI media providers directly and does not include external provider credentials.

## Captions

Visible caption text is rendered locally by the app into the final GIF. External AI media providers are not asked to render readable caption text into the animation.

The backend external-provider adapter sends `captionMode` and `renderCaptionLocally=true` but omits the visible caption string from the provider-facing request.

Caption edits only re-render the final GIF locally and do not submit another AI media-generation request.

## Photos and User Content

Image-to-GIF uses only images selected by the user. The app does not request broad photo library access in v1.

Selected images are downscaled and rewritten before upload to reduce size and strip original metadata.

## App Attest

Deployed backend environments are intended to require App Attest-backed backend sessions. Debug builds use the development App Attest environment; Release builds use the production App Attest environment.

For review builds, confirm the submitted bundle identifiers and backend environment are configured with matching App Attest settings.

## No Image Playground Dependency

Image Playground is not part of the v1 workflow. The repository includes a separate feasibility spike for future evaluation only.

## Test Notes

- No account login is required.
- Use the provided backend environment URL configured in the app settings or default build settings.
- If App Attest is required by the review backend, test on a physical device because Simulator cannot meaningfully validate the production App Attest path.
- The containing app provides onboarding, privacy explanation, settings, and local generation history. The Messages extension is the primary creation flow.

## Support and Privacy URLs

- Support URL: https://github.com/eslutz/Gifster/issues
- Privacy Policy URL: https://github.com/eslutz/Gifster/blob/main/Documentation/PRIVACY_POLICY.md
