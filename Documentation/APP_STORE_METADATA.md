# App Store Connect Metadata Draft

Use this file as the source draft for App Store Connect. Replace placeholder URLs and support contact details before submission.

## App Information

- Name: Gifster
- Subtitle: AI GIFs for Messages
- Category: Social Networking
- Secondary category: Photo & Video
- Content rights: The app generates user-requested media through the developer-operated backend and configured providers.

## Promotional Text

Create custom animated GIFs inside Messages from a prompt, an optional image, and captions you control.

## Description

Gifster is an iMessage app for creating custom animated GIFs without searching a public GIF library.

Open Gifster from the Messages app drawer, describe the GIF you want, optionally select an image, choose a caption mode, preview the finished GIF, and insert it into the Messages compose field as a normal attachment. Messages always requires you to send manually.

Gifster uses local Apple models where available for prompt cleanup, request planning, and caption suggestions. Media generation requests go through the developer-operated backend, which validates requests, applies safety checks, hides provider credentials, and works with configured AI media providers.

Captions are rendered locally into the final GIF, so caption edits do not require a new media-generation request.

## Keywords

gif,gifs,imessage,messages,ai,animation,caption,memes,photos,chat

## What's New

Initial TestFlight build for prompt-based GIF generation in Messages.

## Support URL

TODO: Add public support URL.

## Marketing URL

TODO: Add public product URL, if available.

## Privacy Policy URL

TODO: Publish `Documentation/PRIVACY_POLICY.md` and add the public URL.

## App Review Contact

- First name: TODO
- Last name: TODO
- Phone: TODO
- Email: TODO

## Demo Account

Not required for v1. Gifster does not use account login.

## App Review Notes

Use `Documentation/APP_REVIEW_NOTES.md`.

## App Privacy Answers

These answers must match the deployed backend, provider gateway, and public privacy policy.

- Tracking: No.
- Data linked to user: No, unless the production backend later adds accounts or user-linked retention.
- Data used for tracking: No.
- Data collected for app functionality:
  - User Content: prompts, optional selected images, generated GIF metadata, and caption text.
  - Diagnostics: No third-party diagnostics SDK in this scaffold.
- Photos access: User-selected images only through scoped picker behavior; no broad photo library access for v1.
- Local storage: generated GIF history, active generation state, backend settings, and App Attest key id.

## Review Checklist Before Submission

- Replace all TODO fields.
- Confirm privacy policy URL is publicly reachable.
- Confirm App Store privacy answers match the deployed backend retention policy.
- Confirm App Group and App Attest capabilities are enabled for both bundle identifiers.
- Confirm Release builds use the production App Attest environment.
- Attach screenshots for the containing app and Messages extension flows.
