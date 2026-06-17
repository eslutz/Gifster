# App Store Connect Metadata Draft

Use this file as the source draft for App Store Connect. Replace the GitHub fallback URLs with product-site URLs when a dedicated public site exists. Do not commit private phone numbers to source control; enter that value directly in App Store Connect.

## App Information

- Name: GifForge
- Subtitle: AI GIFs for Messages
- Category: Social Networking
- Secondary category: Photo & Video
- Content rights: The app generates user-requested media through the developer-operated backend and configured providers.

## Promotional Text

Create custom animated GIFs inside Messages from a prompt, an optional image, and captions you control.

## Description

GifForge is an iMessage app for creating custom animated GIFs without searching a public GIF library.

Open GifForge from the Messages app drawer, describe the GIF you want, optionally select an image, choose a caption mode, preview the finished GIF, and insert it into the Messages compose field as a normal attachment. Messages always requires you to send manually.

GifForge uses local Apple models where available for prompt cleanup, request planning, and caption suggestions. Media generation requests go through the developer-operated backend, which validates requests, applies safety checks, hides provider credentials, and works with configured AI media providers.

Captions are rendered locally into the final GIF, so caption edits do not require a new media-generation request.

## Keywords

gif,gifs,imessage,messages,ai,animation,caption,memes,photos,chat

## What's New

Initial TestFlight build for prompt-based GIF generation in Messages.

## Support URL

https://github.com/eslutz/GifForge/issues

## Marketing URL

https://github.com/eslutz/GifForge

## Privacy Policy URL

https://github.com/eslutz/GifForge/blob/main/Documentation/PRIVACY_POLICY.md

## App Review Contact

- First name: Eric
- Last name: Slutz
- Phone: enter directly in App Store Connect; do not commit private phone numbers to source control.
- Email: eric.slutz@icloud.com

## Demo Account

GifForge uses Sign in with Apple. App Review can sign in with a reviewer Apple ID. If the reviewed build points at a backend environment with Apple IAP enabled, make the consumable credit products available in App Store Connect sandbox/review and include any backend URL or test-environment notes in App Review Notes.

## App Review Notes

Use `Documentation/APP_REVIEW_NOTES.md`.

## App Privacy Answers

These answers must match the deployed backend, provider gateway, and public privacy policy.

- Tracking: No.
- Data linked to user: Yes. Sign in with Apple account identifiers, purchase/credit ledger records, and generation ownership records are linked to the GifForge account for app functionality, billing integrity, refund handling, abuse prevention, and support.
- Data used for tracking: No.
- Data collected for app functionality:
  - Contact Info: private relay email metadata if Apple provides it through Sign in with Apple.
  - Identifiers: Apple account subject, internal user id, app account token, backend session metadata.
  - Purchases: Apple IAP transaction ids, product ids, credit grants, reservations, debits, and refund/reversal entries.
  - User Content: prompts, optional selected images, generated GIF metadata, and caption text.
  - Diagnostics: No third-party diagnostics SDK in this scaffold.
- Photos access: User-selected images only through scoped picker behavior; no broad photo library access for v1.
- Local storage: generated GIF history, active generation state, backend settings, App Attest key id, and backend auth tokens in the shared Keychain.

## Review Checklist Before Submission

- Confirm the App Review phone number has been entered directly in App Store Connect.
- Replace GitHub fallback URLs with dedicated product/support/privacy URLs if a public product site is available before submission.
- Confirm privacy policy URL is publicly reachable.
- Confirm App Store privacy answers match the deployed backend retention policy.
- Confirm Sign in with Apple, In-App Purchase, App Group, shared Keychain, and App Attest capabilities are enabled for the required bundle identifiers.
- Confirm the three consumable credit products exist and are review/sandbox-ready: `dev.ericslutz.gifforge.credits.10`, `.25`, and `.60`.
- Confirm Release builds use the production App Attest environment.
- Attach screenshots for the containing app and Messages extension flows.
