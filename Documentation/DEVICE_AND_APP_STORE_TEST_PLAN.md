# Device and App Store Test Plan

Use this plan to collect the remaining evidence needed before treating GifForge as App Store-ready. Record the tested build number, device, iOS version, backend URL, and tester initials for each pass.

## Test Matrix

| Area | Required Evidence |
| --- | --- |
| Containing app | Onboarding/privacy copy, Sign in with Apple, credit balance, IAP credit packs, history, delete confirmation, settings, backend URL, App Attest toggle |
| Messages compact mode | Prompt entry, add image, caption mode, generate, recent GIFs, progress, basic errors |
| Messages expanded mode | Larger editor, source preview, caption suggestions/editing, progress, preview, regenerate, insert |
| Backend | Nonprod URL, App Attest mode, provider adapter, job id, result download |
| Auth/IAP | Sign in with Apple, Keychain token reuse from Messages, StoreKit sandbox purchases, credit grant, reservation, capture/release, refund/reversal handling |
| Privacy | User-selected image only, no broad photo permission prompt, local clear-history behavior |
| App Store | Metadata, support URL, privacy URL, screenshots, App Review notes, privacy nutrition answers |

## Setup

- Build: `$(MARKETING_VERSION)` / `$(CURRENT_PROJECT_VERSION)`
- Git commit:
- Device model:
- iOS version:
- Apple ID / tester type:
- Backend URL:
- Backend image tag:
- App Attest mode: `development`, `production`, or demo bypass
- Account mode: Sign in with Apple sandbox/review/production
- IAP environment: StoreKit local, sandbox, review, or production
- Provider adapter: `fake` or provider name

## Containing App

- [ ] App launches to the GifForge tab.
- [ ] Privacy copy says prompts and selected images may be sent through the GifForge backend.
- [ ] Privacy copy says local Apple models are used where available.
- [ ] History tab loads generated GIF history.
- [ ] Clear action asks for confirmation before deleting local history.
- [ ] Confirming clear removes generated GIF history and active-job metadata.
- [ ] Settings tab allows editing backend base URL.
- [ ] Settings tab allows toggling App Attest requirement.
- [ ] Sign in with Apple completes from the containing app.
- [ ] Account tokens are restored after app relaunch without re-signing in.
- [ ] Credit balance loads from the backend.
- [ ] Credit pack products load from StoreKit.
- [ ] No unexpected broad photo-library permission prompt appears from the containing app.

Evidence:

- Screenshot:
- Notes:

## Messages Extension: Compact Mode

- [ ] Open Messages and select GifForge from the app drawer.
- [ ] Compact mode shows prompt entry.
- [ ] Compact mode exposes add-image control.
- [ ] Compact mode exposes caption mode selection: no caption, user text, AI suggestion.
- [ ] Generate button is disabled for an empty prompt.
- [ ] Generate button starts a backend job for a valid prompt.
- [ ] Progress state is visible while planning/submitting/generating/rendering.
- [ ] Recent GIFs are visible after at least one completed generation.
- [ ] Basic backend/network errors are visible and actionable.
- [ ] No sticker UI or sticker export path is visible.

Evidence:

- Prompt:
- Job id:
- Screenshot:
- Notes:

## Messages Extension: Expanded Mode

- [ ] Expanded mode shows a larger prompt editor.
- [ ] Selected source image preview is visible for image-to-GIF.
- [ ] Caption suggestions can be requested.
- [ ] Suggested captions can be reviewed, selected, and edited.
- [ ] Explicit caption text is preserved unless too long or unsafe.
- [ ] Caption edits re-render the GIF locally with Apply Caption and do not create a new backend job.
- [ ] Finished GIF preview is visible.
- [ ] Regenerate starts a new backend generation job.
- [ ] Insert adds the GIF to the Messages compose field as an attachment.
- [ ] Messages requires manual send after insertion.
- [ ] No auto-send behavior occurs.

Evidence:

- Prompt:
- Source image used:
- Caption mode:
- Job id:
- Screenshot:
- Notes:

## Resume and Job State

- [ ] Start a generation job.
- [ ] Close the Messages extension while the job is active.
- [ ] Reopen GifForge from Messages.
- [ ] The extension resumes polling the existing job instead of creating a duplicate job.
- [ ] Completed result renders and can be inserted.
- [ ] Failed active jobs show a user-facing error and can be cleared.
- [ ] Backend-expired active jobs are not resumed after the stored `expiresAt` time.

Evidence:

- Original job id:
- Reopened job id:
- Screenshot:
- Notes:

## Auth, Credits, and IAP

- [ ] Backend is deployed with `GIFFORGE_AUTH_REQUIRED=true`.
- [ ] Sign in with Apple sends a nonce and succeeds against the backend.
- [ ] Backend tokens are stored in shared Keychain and not in `UserDefaults`.
- [ ] Messages extension can submit protected requests using the shared backend token.
- [ ] Unauthenticated protected requests return HTTP 401.
- [ ] StoreKit products load for `dev.ericslutz.gifforge.credits.10`, `.25`, and `.60`.
- [ ] Purchase submits StoreKit signed transaction payload before finishing the transaction.
- [ ] Backend grants credits only after transaction verification.
- [ ] Duplicate transaction submission is idempotent.
- [ ] Product-id mismatch and app-account-token mismatch are rejected.
- [ ] Generation request with zero available credits returns HTTP 402.
- [ ] Accepted generation reserves one credit and reduces available balance.
- [ ] Successful provider result captures the reservation as a debit.
- [ ] Terminal provider/backend failure releases the reservation.
- [ ] Refund/revoke notification inserts a reversal; negative available balance blocks new generations.

Evidence:

- Apple account type:
- Product ids tested:
- Transaction ids or redacted hashes:
- Credit balance before/after:
- Job id:
- Notes:

## App Attest Physical Device

- [ ] Backend is deployed with `GIFFORGE_APP_ATTEST_REQUIRED=true`.
- [ ] Backend has the expected deployed app identifier configured in `TeamID.BundleID` form.
- [ ] Backend has the Apple App Attest root certificate configured.
- [ ] Debug/device build uses the expected App Attest environment.
- [ ] App receives an App Attest challenge.
- [ ] App exchanges attestation for a backend session token.
- [ ] Generation, status, and result routes succeed with the session token.
- [ ] Requests without a valid session token are rejected with HTTP 401.
- [ ] Simulator behavior is documented as unsupported for production App Attest validation.

Evidence:

- Bundle id:
- App Attest environment:
- Backend deployment:
- Job id:
- Notes:

## Apple Developer Portal

- [ ] `scripts/validate-client-signing.rb` passes before archiving.
- [ ] `Client/project.yml` and `Client/Extensions/GifForgeMessages/Info.plist` confirm the Messages extension metadata is configured for `com.apple.message-payload-provider`.
- [ ] Containing app bundle id exists.
- [ ] Messages extension bundle id exists and is prefixed by the containing app bundle id.
- [ ] App Group capability is enabled for both bundle ids.
- [ ] Sign in with Apple is enabled for the containing app where required.
- [ ] In-App Purchase is enabled for the containing app.
- [ ] Shared Keychain access group is enabled for the containing app and Messages extension.
- [ ] App Attest capability is enabled where required.
- [ ] App Group identifier matches `group.dev.ericslutz.gifforge`.
- [ ] Release signing uses the intended team and provisioning profiles.
- [ ] Archive validates without app-extension bundle id or entitlement errors.

Evidence:

- Team id:
- Containing app bundle id:
- Extension bundle id:
- App Group id:
- Archive path or Organizer validation:
- Notes:

## App Store Connect

- [ ] App name, subtitle, category, and keywords are final.
- [ ] Support URL is public and reachable.
- [ ] Privacy policy URL is public and reachable.
- [ ] App Review contact fields are complete.
- [ ] Screenshots cover containing app and Messages extension flows.
- [ ] App Review notes include attachment insertion, manual sending, backend-mediated AI generation, App Attest, no sticker mode, and no Image Playground dependency.
- [ ] Consumable IAP products are configured for `dev.ericslutz.gifforge.credits.10`, `.25`, and `.60`.
- [ ] App Review notes explain Sign in with Apple and Apple IAP credit packs.
- [ ] App privacy answers match the deployed backend retention and provider-sharing behavior.
- [ ] Privacy nutrition answers do not claim tracking.

Evidence:

- Support URL:
- Privacy URL:
- Screenshot set:
- Notes:

## Structured Evidence Template

Create a structured evidence file before the physical-device pass:

```bash
scripts/validate-device-evidence.rb --template Documentation/DeviceEvidence/nonprod-device-pass.json
```

`Documentation/DeviceEvidence/` is ignored by git. Keep private contact details, credentials, bearer tokens, and raw App Attest material out of the evidence file; store those only in App Store Connect, GitHub environment secrets, Azure, or the release record system.

After the pass, fill every required field and run:

```bash
scripts/validate-device-evidence.rb Documentation/DeviceEvidence/nonprod-device-pass.json
```

The validator requires containing-app, compact Messages, expanded Messages, resume/job-state, physical App Attest, Apple Developer portal, and App Store Connect evidence before the pass can count toward release readiness.

### Containing-App Screenshot Capture

Run the deterministic containing-app screenshot pass before assembling App Store Connect screenshots:

```bash
scripts/capture-app-store-screenshots.sh
```

The script writes PNG files to `Documentation/AppStoreScreenshots/containing-app` by default and preserves an XCTest result bundle under `/private/tmp/gifforge-app-store-screenshots.xcresult`. Override the simulator with `GIFFORGE_SCREENSHOT_DESTINATION` or pass a custom output directory as the first argument.

This script covers the containing app overview, seeded history, clear-history confirmation, and settings screens. Capture Messages extension screenshots separately from the physical Messages flow above because the App Store screenshot set must show the real compact/expanded iMessage extension experience.

### App Store Submission Package

After containing-app and Messages extension screenshots are available, assemble the manual App Store Connect handoff:

```bash
scripts/export-app-store-submission-package.rb --require-screenshots
```

The package is written under ignored `Documentation/AppStoreSubmission/` by default and includes structured metadata, App Review notes, privacy policy text, screenshot copies, and a manifest of any remaining blockers.

## Pass Criteria

GifForge is ready for App Store submission only after every checklist item above is complete, evidence is attached or linked, and `Documentation/APP_STORE_READINESS.md` no longer lists unresolved release blockers.
