# App Store Readiness

## Automated Gates

- Backend xUnit tests cover health, moderation, fake provider behavior, durable Table Storage job state, shared App Attest state storage, queue worker processing and retry behavior, App Attest authorization gates, demo-only App Attest bypass behavior, and cryptographic App Attest verifier checks using a generated certificate fixture.
- Swift package tests cover prompt planning fallback, backend authorization, App Attest client routes, active-job persistence, MP4 ingestion, frame-sequence rendering, GIF downsampling, caption line fitting, and user-facing error copy for provider downtime, unavailable local models, network failures, moderation rejections, and App Attest unavailable states.
- The Xcode scheme includes `GifsterUITests` for the containing app shell, history tab, settings tab, backend URL field, and App Attest setting.
- The app and Messages extension include privacy manifests declaring no tracking, app-functionality use of user content/images, and the UserDefaults required-reason API.
- App group entitlements are configured for the containing app and Messages extension. App Attest entitlement uses `development` for Debug and `production` for Release.
- App Store metadata, App Review notes, and a public privacy policy draft are maintained in `Documentation/APP_STORE_METADATA.md`, `Documentation/APP_REVIEW_NOTES.md`, and `Documentation/PRIVACY_POLICY.md`.
- Physical-device and App Store evidence should be collected with `Documentation/DEVICE_AND_APP_STORE_TEST_PLAN.md`.
- The client workflow regenerates the Xcode project, checks generated files, runs Swift package tests, and builds the app, Messages extension, and UI tests for iOS Simulator.
- `scripts/smoke-backend.sh` covers the backend demo loop by checking `/health`, submitting a fake-provider generation job, polling status, and downloading the generated frame-sequence result.
- The manual `Deploy Nonprod` workflow deploys the selected GHCR backend image to the existing `rg-gifster-nonprod` resource group and runs the backend smoke test against the resulting Container Apps URL.

## Verified Nonprod Evidence

- Backend commit `99e376d592c1ec55a9e5e51b6a6c1a0105a42f75` passed GitHub Actions backend run `27526392923`, including build, xUnit tests, Native AOT publish, and GHCR image publish.
- PR feedback commit `14cc2952c2311bb78532ca389b09566e51cb4579` passed PR checks for Backend x2, Client, and Infrastructure. That commit adds shared App Attest state storage, retryable queue-worker failure handling, GIF duration-preserving frame sampling, and scale-to-zero Container Apps defaults.
- Nonprod was redeployed with image `ghcr.io/eslutz/gifster-backend:14cc2952c2311bb78532ca389b09566e51cb4579` using Azure deployment `gifster-nonprod-scalezero-202606150620`.
- Resource group: `rg-gifster-nonprod`.
- API Container App: `gifster-nonprod-mamh4mnpf-api`.
- Worker Container App: `gifster-nonprod-mamh4mnpf-worker`.
- Nonprod URL: `https://gifster-nonprod-mamh4mnpf-api.greencliff-56b7d6e3.eastus.azurecontainerapps.io`.
- API Container App reports image `ghcr.io/eslutz/gifster-backend:14cc2952c2311bb78532ca389b09566e51cb4579`, `minReplicas=0`, `maxReplicas=5`, and HTTP scale rule `concurrentRequests=50`.
- Worker Container App reports image `ghcr.io/eslutz/gifster-backend:14cc2952c2311bb78532ca389b09566e51cb4579`, `minReplicas=0`, `maxReplicas=5`, and Azure Queue scale rule `generation-jobs` with queue length `1` and user-assigned managed identity.
- `/health` returned `{"ok":true,"provider":"fake-frame-sequence","mode":"demo"}` with HTTP 200.
- `scripts/smoke-backend.sh` passed against the scale-to-zero nonprod deployment with demo App Attest enabled for job `7251d569-de51-4e2f-bde6-1811094cf10e`.
- Attempted to dispatch `deploy-nonprod.yml` from `main` as GitHub Actions run `27540776242`; the run reached `azure/login@v2` and failed because `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, and `AZURE_SUBSCRIPTION_ID` were not configured for the `nonprod` environment.
- The workflow now uses resource-group-scope deployment against `rg-gifster-nonprod` so the GitHub OIDC identity can use resource-group-scoped `Contributor` and `Role Based Access Control Administrator` grants instead of subscription-scoped grants. Workflow-dispatch proof remains pending until the Azure federated credential, GitHub environment secrets, and scoped RBAC assignments are configured.

## Required Physical Device Checks

Use `Documentation/DEVICE_AND_APP_STORE_TEST_PLAN.md` to record device, build, backend, screenshots, job ids, App Attest evidence, and App Store Connect evidence for these checks.

- Open Gifster from the Messages app drawer in compact mode.
- Enter a prompt, generate with the fake or nonprod backend, preview the GIF, insert it with attachment insertion, and confirm Messages requires manual send.
- Repeat in expanded mode with a selected image and each caption mode: no caption, user text, and AI-suggested text.
- Close and reopen the Messages extension during generation and verify active job resume.
- Verify App Attest-enabled backend access on a real device with the deployed app identifier and Apple App Attest root certificate configured.
- Confirm no sticker APIs or sticker mode are visible in v1.

## App Review Notes

Use `Documentation/APP_REVIEW_NOTES.md` as the submission draft.

- Gifster inserts GIFs into the Messages compose field using attachment insertion only; it never auto-sends messages.
- The Messages extension is the primary creation flow, and the containing app provides onboarding, privacy disclosure, history, and settings.
- Prompts and selected images may be sent to the developer-operated backend and onward to external AI media providers; provider credentials are never shipped in the iOS app.
- Captions are rendered locally into the final GIF; external providers should not be asked to render readable text.
- The app uses only user-selected images through PhotosPicker and does not request broad photo library access for v1.
- App Attest protects deployed backend access when `GIFSTER_APP_ATTEST_APP_IDENTIFIER` and `GIFSTER_APP_ATTEST_ROOT_CERTIFICATE_PEM` are configured. Local development can run without it, and the demo session bypass must remain disabled outside controlled development/testing.

## Release Blockers

- Complete at least one physical-device Messages pass for compact and expanded modes.
- Configure production App Attest app identifier/root certificate values and validate the flow on a physical device.
- Validate production signing, bundle identifiers, App Group/App Attest capabilities, and extension metadata in the Apple Developer portal.
- Replace all App Store metadata TODOs, publish the privacy policy URL, and confirm in-app wording matches backend retention and deletion behavior.
- Configure the GitHub OIDC federated credential, nonprod environment secrets, and `rg-gifster-nonprod`-scoped Azure RBAC grants, then run the `Deploy Nonprod` workflow with an immutable GHCR image tag and preserve the successful workflow run as deployment evidence.
- Smoke-test GIF preview and Messages insertion from a device against the nonprod backend.
