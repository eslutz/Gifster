# Device and App Store Test Plan

Use this plan to collect the remaining evidence needed before treating Gifster as App Store-ready. Record the tested build number, device, iOS version, backend URL, and tester initials for each pass.

## Test Matrix

| Area | Required Evidence |
| --- | --- |
| Containing app | Onboarding/privacy copy, history, delete confirmation, settings, backend URL, App Attest toggle |
| Messages compact mode | Prompt entry, add image, caption mode, generate, recent GIFs, progress, basic errors |
| Messages expanded mode | Larger editor, source preview, caption suggestions/editing, progress, preview, regenerate, insert |
| Backend | Nonprod URL, App Attest mode, provider adapter, job id, result download |
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
- Provider adapter: `fake` or provider name

## Containing App

- [ ] App launches to the Gifster tab.
- [ ] Privacy copy says prompts and selected images may be sent through the Gifster backend.
- [ ] Privacy copy says local Apple models are used where available.
- [ ] History tab loads generated GIF history.
- [ ] Clear action asks for confirmation before deleting local history.
- [ ] Confirming clear removes generated GIF history and active-job metadata.
- [ ] Settings tab allows editing backend base URL.
- [ ] Settings tab allows toggling App Attest requirement.
- [ ] No unexpected broad photo-library permission prompt appears from the containing app.

Evidence:

- Screenshot:
- Notes:

## Messages Extension: Compact Mode

- [ ] Open Messages and select Gifster from the app drawer.
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
- [ ] Caption edits re-render the GIF locally without creating a new backend job.
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
- [ ] Reopen Gifster from Messages.
- [ ] The extension resumes polling the existing job instead of creating a duplicate job.
- [ ] Completed result renders and can be inserted.
- [ ] Failed or expired active jobs show a user-facing error and can be cleared.

Evidence:

- Original job id:
- Reopened job id:
- Screenshot:
- Notes:

## App Attest Physical Device

- [ ] Backend is deployed with `GIFSTER_APP_ATTEST_REQUIRED=true`.
- [ ] Backend has the production app identifier configured in `TeamID.BundleID` form.
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
- [ ] `scripts/verify-release-readiness.rb` passes before archiving and confirms the Messages extension metadata is configured for `com.apple.message-payload-provider`.
- [ ] Containing app bundle id exists.
- [ ] Messages extension bundle id exists and is prefixed by the containing app bundle id.
- [ ] App Group capability is enabled for both bundle ids.
- [ ] App Attest capability is enabled where required.
- [ ] App Group identifier matches `group.dev.ericslutz.Gifster` or the intentionally configured production replacement.
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
- [ ] App privacy answers match the deployed backend retention and provider-sharing behavior.
- [ ] Privacy nutrition answers do not claim tracking.

Evidence:

- Support URL:
- Privacy URL:
- Screenshot set:
- Notes:

## Pass Criteria

Gifster is ready for App Store submission only after every checklist item above is complete, evidence is attached or linked, and `Documentation/APP_STORE_READINESS.md` no longer lists unresolved release blockers.
