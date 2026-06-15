# Development and Demo Setup

## Prerequisites

- Xcode 26.5 or later for iOS app and extension builds.
- XcodeGen 2.45 or later.
- Swift 6.
- .NET 10 SDK for the ASP.NET Core Minimal API backend.
- Azure CLI with Bicep support for infrastructure validation/deployment.

The current scaffold was generated and build-checked on Xcode 26.5 with the iOS 26.5 SDK.

## Generate the Xcode Project

```bash
cd Client
xcodegen generate
open Gifster.xcodeproj
```

Select the `Gifster` scheme. Configure signing for the app and extension bundle ids:

- `dev.ericslutz.gifforge`
- `dev.ericslutz.gifforge.messagesextension`

The shared App Group is `group.dev.ericslutz.gifforge`.

The XcodeGen project declares the shared App Group and App Attest capability for both targets. `APP_ATTEST_ENVIRONMENT` is `development` for Debug and `production` for Release, so confirm both values are allowed by the Apple Developer portal before archiving.

Validate the source signing configuration before device or archive work:

```bash
scripts/validate-client-signing.rb
```

The Messages extension bundle id must be prefixed by the containing app bundle id, and both targets must share the same App Group. If you intentionally change the production bundle id or App Group, update `Client/project.yml`, regenerate the Xcode project, and keep both entitlement files plus `AppStorageDirectories.appGroupIdentifier` in sync.

Run the release readiness invariant check before screenshots, archive validation, or App Store submission prep:

```bash
ruby scripts/verify-release-readiness.rb
```

The readiness check verifies the iOS 26.5 target, v1 no-sticker/no-Image-Playground source-code invariants, iMessage extension metadata, local caption re-render wiring, backend expiration propagation, deploy workflow scale-to-zero and production safety invariants, provider health/preflight invariants, containing-app screenshot tooling, App Store metadata validation, known App Store metadata placeholders, and tracked app/iMessage icon catalog completeness.

## Run Shared Swift Tests

```bash
cd Client/Packages/GifsterCore
swift test --scratch-path /private/tmp/gifster-swiftpm
```

## Run Backend Tests

```bash
dotnet test Backend.Tests/Gifster.Backend.Tests.csproj
```

## Capture Containing-App Screenshots

```bash
scripts/capture-app-store-screenshots.sh
```

By default, the script writes App Store prep screenshots to `Documentation/AppStoreScreenshots/containing-app`, which is ignored by git. Set `GIFSTER_SCREENSHOT_DESTINATION` to target a specific simulator, or pass an output directory as the first argument. Capture Messages extension screenshots separately on a physical device from Messages compact and expanded modes.

## Validate App Store Metadata

```bash
scripts/validate-app-store-metadata.rb
```

The validator checks App Store field lengths, required support/privacy URLs, no-tracking privacy claims, review-note coverage for attachment insertion and manual sending, no sticker mode, no Image Playground dependency, and privacy-policy retention/provider disclosures.

## Export App Store Submission Package

```bash
scripts/export-app-store-submission-package.rb \
  --containing-screenshots Documentation/AppStoreScreenshots/containing-app \
  --messages-screenshots Documentation/AppStoreScreenshots/messages-extension
```

The exporter writes an ignored package under `Documentation/AppStoreSubmission/` by default. It copies public metadata, App Review notes, privacy policy text, and available screenshot PNGs into a single manual App Store Connect handoff. Use `--require-screenshots` for the final release pass; that mode fails until both containing-app screenshots and physical Messages compact/expanded screenshots are available.

## Validate Physical-Device Evidence

```bash
scripts/validate-device-evidence.rb --template Documentation/DeviceEvidence/nonprod-device-pass.json
scripts/validate-device-evidence.rb Documentation/DeviceEvidence/nonprod-device-pass.json
```

The generated evidence directory is ignored by git. Use the template while testing the containing app, Messages compact mode, Messages expanded mode, resume behavior, physical-device App Attest, Apple Developer portal capabilities, and App Store Connect readiness. Do not place private phone numbers, credentials, authorization headers, bearer tokens, or App Attest secrets in the evidence file.

## Run the Local Backend

```bash
ASPNETCORE_HTTP_PORTS=8787 dotnet run --project Backend/Gifster.Backend.csproj
```

The backend listens at `http://127.0.0.1:8787`.

## Backend Deployment Direction

The production backend target is ASP.NET Core Minimal API with Native AOT on Azure Container Apps. Keep the public API thin and stateless, then use Azure Queue Storage for asynchronous provider orchestration, Blob Storage for temporary media/result handoff, and Table Storage for durable job state. Store provider credentials in Container Apps secrets or Key Vault and use managed identity for Azure resource access.

Provider adapter selection:

- `GIFSTER_PROVIDER_ADAPTER=fake`: deterministic local/demo frame-sequence provider.
- `GIFSTER_PROVIDER_ADAPTER=external-http`: posts generation requests to a compatible provider gateway and downloads either `video/mp4` or `application/vnd.gifster.frame-sequence+json` results.

Validate a compatible provider gateway before using it in nonprod or prod:

```bash
GIFSTER_EXTERNAL_PROVIDER_SUBMIT_URL=https://provider.example.test/jobs \
GIFSTER_EXTERNAL_PROVIDER_RESULT_URL_TEMPLATE='https://provider.example.test/jobs/{providerJobId}/result' \
GIFSTER_EXTERNAL_PROVIDER_AUTHORIZATION='Bearer <token>' \
scripts/validate-external-provider-contract.rb
```

Use `scripts/validate-external-provider-contract.rb --print-payload` to inspect the sanitized provider-facing JSON without network calls. To validate image-to-GIF, pass `--mode image_to_gif` and provide `GIFSTER_PROVIDER_PRECHECK_IMAGE_BASE64`, `GIFSTER_PROVIDER_PRECHECK_IMAGE_WIDTH`, and `GIFSTER_PROVIDER_PRECHECK_IMAGE_HEIGHT` for an app-processed JPEG sample.

The provider preflight polls retryable result states until the configured timeout and accepts only non-empty `video/mp4` or valid `application/vnd.gifster.frame-sequence+json` results.

Record the selected provider decision and production readiness evidence:

```bash
scripts/validate-provider-onboarding.rb --template Documentation/ProviderEvidence/first-provider.json
scripts/validate-provider-onboarding.rb Documentation/ProviderEvidence/first-provider.json
```

The generated evidence directory is ignored by git. Keep provider credentials, Authorization header values, API keys, bearer tokens, passwords, and raw secret values out of the evidence file.

Deployed environments set `GIFSTER_APP_ATTEST_REQUIRED=true`. Local development leaves it unset unless you are testing the App Attest challenge/session flow.

Real App Attest verification requires:

- `GIFSTER_APP_ATTEST_APP_IDENTIFIER`: Apple Team ID plus bundle id, such as `TEAMID.dev.ericslutz.gifforge`.
- `GIFSTER_APP_ATTEST_ROOT_CERTIFICATE_PEM`: PEM-encoded Apple App Attest root certificate.

The scaffold includes a demo-only App Attest bypass for local and nonprod smoke testing:

```bash
GIFSTER_APP_ATTEST_REQUIRED=true \
GIFSTER_APP_ATTEST_DEMO_BYPASS=true \
ASPNETCORE_HTTP_PORTS=8787 \
dotnet run --project Backend/Gifster.Backend.csproj
```

`GIFSTER_APP_ATTEST_DEMO_BYPASS` lets the backend issue short-lived demo session tokens from placeholder attestation payloads. Do not set it in production.

Pushes to `main` publish the backend container to GitHub Container Registry as:

- `ghcr.io/eslutz/gifster-backend:latest`
- `ghcr.io/eslutz/gifster-backend:<commit-sha>`

## Validate Infrastructure

```bash
az bicep build --file infra/main.bicep
az bicep build --file infra/main.subscription.bicep
```

Bootstrap an environment with `az deployment sub create` after setting the `containerImage` parameter to a pushed backend image, preferably the immutable commit SHA tag from GHCR for repeatable deployments. Use `infra/main.subscription.bicep` for initial environment creation; it creates `rg-gifster-nonprod` or `rg-gifster-prod` and then deploys `infra/main.bicep` into that resource group. Once the resource group exists, deploy normal updates with `az deployment group create --resource-group rg-gifster-nonprod --template-file infra/main.bicep ...`. Deployments default to `minReplicas=0` and `workerMinReplicas=0` to scale to zero; the worker wakes from the Azure Queue scale rule when generation jobs are waiting.

Deployed environments also default to `generationJobRetentionHours=24`, `temporaryBlobRetentionDays=2`, `retentionCleanupIntervalMinutes=360`, and `retentionCleanupBatchSize=100`. The backend stores an `expiresAt` value with each generation job, returns HTTP `410 Gone` for expired status/result reads, and prunes expired job rows during cleanup passes. Azure Storage lifecycle policy deletes temporary provider result and source-image blobs from the private containers.

The `Deploy Nonprod` GitHub Actions workflow can deploy and smoke-test `rg-gifster-nonprod` manually. It uses resource-group-scope deployment against the existing nonprod resource group. Run `scripts/setup-azure-oidc.sh --environment nonprod` first in dry-run mode to review the Azure OIDC trust, GitHub environment secrets, and resource-group-scoped RBAC changes. After approval, run it with `--apply` to configure `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, the federated credential subject `repo:eslutz/Gifster:environment:nonprod`, and the `Contributor` plus `Role Based Access Control Administrator` roles at the `rg-gifster-nonprod` scope. Dispatch the workflow with an immutable backend GHCR commit SHA tag to deploy. Use its demo App Attest bypass input only for controlled nonprod smoke tests.

The `Deploy Prod` workflow deploys to `rg-gifster-prod` through the `prod` GitHub environment. Run `scripts/setup-azure-oidc.sh --environment prod` first, configure the required production App Attest and external provider secrets in that GitHub environment, and dispatch only with an immutable commit SHA image tag. Production deployment forces `providerAdapter=external-http`, disables the demo App Attest bypass, and performs only a `/health` check; generation validation still requires a physical-device App Attest session and the selected provider.

Audit OIDC readiness without mutating Azure or GitHub state:

```bash
scripts/audit-azure-oidc-readiness.rb --environment nonprod --strict
scripts/audit-azure-oidc-readiness.rb --environment prod --strict
```

The audit checks the GitHub environment, required environment secret names, Azure app registration, service principal, federated credential subject, and resource-group-scoped `Contributor` plus `Role Based Access Control Administrator` assignments. It writes JSON under the ignored `Documentation/DeploymentEvidence/` folder and records secret names only, not secret values.

After a successful deploy workflow, capture read-only release evidence:

```bash
scripts/collect-deployment-evidence.rb \
  --environment nonprod \
  --workflow-run-id <github-actions-run-id>
```

The default output directory `Documentation/DeploymentEvidence/` is ignored by git. The JSON includes local git context, selected GitHub Actions run metadata, sanitized Container Apps image/scale/rule settings, and `/health` output without serializing Container Apps environment variable values.

## App Store Submission Drafts

- `Documentation/APP_STORE_METADATA.md` contains App Store Connect copy, keywords, privacy-answer notes, public GitHub fallback URLs, and owner-entered fields that should not be committed to source control.
- `Documentation/APP_REVIEW_NOTES.md` contains review notes for attachment insertion, manual sending, backend-mediated AI generation, App Attest, and no sticker mode.
- `Documentation/PRIVACY_POLICY.md` contains the public privacy policy draft to publish before submission.

## Smoke Test the Backend

With a local backend running without App Attest enforcement:

```bash
scripts/smoke-backend.sh
```

With the local demo App Attest bypass enabled:

```bash
GIFSTER_BACKEND_URL=http://127.0.0.1:8787 \
GIFSTER_SMOKE_USE_DEMO_APP_ATTEST=true \
scripts/smoke-backend.sh
```

For deployed environments, set `GIFSTER_BACKEND_URL` to the Container Apps URL. Once real App Attest verification exists, provide a short-lived real session token with `GIFSTER_APP_ATTEST_SESSION_TOKEN`.

## CI

GitHub Actions is split into path-scoped workflows:

- `Backend` runs for `Backend/**`, `Backend.Tests/**`, `.dockerignore`, and backend workflow changes.
- `Infrastructure` runs for `infra/**` and infrastructure workflow changes.
- `Client` runs for `Client/**` and client workflow changes.
- `Deploy Nonprod` is manual-only and deploys the selected backend image to Azure, then smoke-tests the Container Apps URL.
- `Deploy Prod` is manual-only and deploys an immutable backend image tag to Azure production, then health-checks the Container Apps URL.

The client workflow also builds the containing app, Messages extension, and UI test target for iOS Simulator. Documentation-only changes do not run these workflows. Pushes to `main` that touch backend code also authenticate to GHCR and publish the backend image.

## End-to-End Demo Flow

1. Start the local backend with `ASPNETCORE_HTTP_PORTS=8787 dotnet run --project Backend/Gifster.Backend.csproj`.
2. Launch the containing app once and confirm the backend URL in Settings.
3. Open Messages, select the Gifster iMessage app, and enter a prompt.
4. Optionally add an image with the Photos picker.
5. Select a caption mode.
6. Tap Generate.
7. The extension plans a structured request, submits a backend job, polls until completion, downloads a fake frame sequence from the demo provider, renders a local GIF, and shows a preview.
8. Tap Insert to add the GIF to the Messages compose field.
9. Send manually from Messages.
