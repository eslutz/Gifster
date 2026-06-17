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
open GifForge.xcodeproj
```

Select the `GifForge` scheme. Configure signing for the app and extension bundle ids:

- `dev.ericslutz.gifforge`
- `dev.ericslutz.gifforge.messagesextension`

The shared App Group is `group.dev.ericslutz.gifforge`.

The XcodeGen project declares the shared App Group and App Attest capability for both targets. `APP_ATTEST_ENVIRONMENT` is `development` for Debug and `production` for Release, so confirm both values are allowed by the Apple Developer portal before archiving.

Validate the source signing configuration before device or archive work:

```bash
scripts/validate-client-signing.rb
```

The Messages extension bundle id must be prefixed by the containing app bundle id, and both targets must share the same App Group and Keychain access group. If you intentionally change the production bundle id, App Group, or shared Keychain group, update `Client/project.yml`, regenerate the Xcode project, and keep both entitlement files plus `AppStorageDirectories` in sync.

Before screenshots, archive validation, or App Store submission prep, run the focused validators for the area you are touching. Use `scripts/validate-client-signing.rb` for signing and entitlement checks, `scripts/validate-app-store-metadata.rb` for metadata/review/privacy copy, and `scripts/validate-device-evidence.rb --template <path>` for physical-device evidence structure.

## Run Shared Swift Tests

```bash
cd Client/Packages/GifForgeCore
swift test --scratch-path /private/tmp/gifforge-swiftpm
```

## Run Backend Tests

```bash
dotnet test Backend.Tests/GifForge.Backend.Tests.csproj
```

## Auth, IAP, and SQL

Local backend tests use explicit demo Apple auth/IAP bypass flags. Deployed nonprod/prod must leave `GIFFORGE_AUTH_DEMO_BYPASS=false` and `GIFFORGE_IAP_DEMO_BYPASS=false`.

Required deployed settings:

- `GIFFORGE_AUTH_REQUIRED=true`
- `GIFFORGE_SQL_SERVER=ericslutz-dev-db.database.windows.net` for nonprod
- `GIFFORGE_SQL_DATABASE=ericslutz.dev.db` for nonprod
- `GIFFORGE_APPLE_ID_TOKEN_AUDIENCES=dev.ericslutz.gifforge`
- `GIFFORGE_APP_STORE_BUNDLE_ID=dev.ericslutz.gifforge`
- `GIFFORGE_APP_ATTEST_REQUIRED=true`

Production must use a separate production SQL database before real users or live purchases.

Apply the SQL migration with a migration principal, not the runtime managed identity:

```bash
Backend/Database/Migrations/001_gifforge_accounts_iap_credits.sql
```

Validate nonprod SQL readiness without storing credentials or secret values:

```bash
scripts/validate-sql-readiness.rb --environment nonprod --strict
```

The validator checks the configured Azure SQL server/database, required `gifforge` tables, product ids, and migration file contents. It writes sanitized evidence under ignored `Documentation/DeploymentEvidence/`.

StoreKit sandbox validation is still required before release: Sign in with Apple on a sandbox-capable device, buy each consumable product id, confirm the client submits the StoreKit signed transaction before finishing it, confirm backend credit grants are idempotent, and confirm refunds/revocations arrive through App Store Server Notifications.

## Capture Containing-App Screenshots

```bash
scripts/capture-app-store-screenshots.sh
```

By default, the script writes App Store prep screenshots to `Documentation/AppStoreScreenshots/containing-app`, which is ignored by git. Set `GIFFORGE_SCREENSHOT_DESTINATION` to target a specific simulator, or pass an output directory as the first argument. Capture Messages extension screenshots separately on a physical device from Messages compact and expanded modes.

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
ASPNETCORE_HTTP_PORTS=8787 dotnet run --project Backend/GifForge.Backend.csproj
```

The backend listens at `http://127.0.0.1:8787`.

## Backend Deployment Direction

The production backend target is ASP.NET Core Minimal API with Native AOT on Azure Container Apps. Keep the public API thin and stateless, then use Azure Queue Storage for asynchronous provider orchestration, Blob Storage for temporary media/result handoff, and Table Storage for durable job state. Store provider credentials in Container Apps secrets or Key Vault and use managed identity for Azure resource access.

Provider routing starts directly with the fal.ai/Luma video provider router. Configure provider API keys in Azure Key Vault, provider enabled flags and model cost overrides in Azure App Configuration, and leave provider/model IDs in the backend C# model catalog.

Before enabling paid providers in nonprod or prod, create and validate provider onboarding evidence:

```bash
scripts/validate-provider-onboarding.rb --template Documentation/ProviderEvidence/direct-video.json
scripts/validate-provider-onboarding.rb Documentation/ProviderEvidence/direct-video.json
```

The provider preflight polls retryable result states until the configured timeout and accepts only non-empty `video/mp4` or valid `application/vnd.gifforge.frame-sequence+json` results.

Record the selected provider decision and production readiness evidence:

```bash
scripts/validate-provider-onboarding.rb --template Documentation/ProviderEvidence/first-provider.json
scripts/validate-provider-onboarding.rb Documentation/ProviderEvidence/first-provider.json
```

The generated evidence directory is ignored by git. Keep provider credentials, Authorization header values, API keys, bearer tokens, passwords, and raw secret values out of the evidence file.

Deployed environments set `GIFFORGE_APP_ATTEST_REQUIRED=true`. Local development leaves it unset unless you are testing the App Attest challenge/session flow.

Real App Attest verification requires:

- `GIFFORGE_APP_ATTEST_APP_IDENTIFIER`: Apple Team ID plus bundle id, such as `TEAMID.dev.ericslutz.gifforge`.
- `GIFFORGE_APP_ATTEST_ROOT_CERTIFICATE_PEM`: PEM-encoded Apple App Attest root certificate.

The scaffold includes a demo-only App Attest bypass for local smoke testing:

```bash
GIFFORGE_APP_ATTEST_REQUIRED=true \
GIFFORGE_APP_ATTEST_DEMO_BYPASS=true \
ASPNETCORE_HTTP_PORTS=8787 \
dotnet run --project Backend/GifForge.Backend.csproj
```

`GIFFORGE_APP_ATTEST_DEMO_BYPASS` lets the backend issue short-lived demo session tokens from placeholder attestation payloads. Do not set it in nonprod or production.

Pushes to `main` publish the backend container to GitHub Container Registry as:

- `ghcr.io/eslutz/gifforge-backend:latest`
- `ghcr.io/eslutz/gifforge-backend:<commit-sha>`

## Validate Infrastructure

```bash
az bicep build --file infra/main.bicep
az bicep build --file infra/main.subscription.bicep
```

Bootstrap an environment with `az deployment sub create` after setting the `containerImage` parameter to a pushed backend image, preferably the immutable commit SHA tag from GHCR for repeatable deployments. Use `infra/main.subscription.bicep` for initial environment creation; it creates `rg-gifforge-nonprod` or `rg-gifforge-prod` and then deploys `infra/main.bicep` into that resource group. Once the resource group exists, deploy normal updates with `az deployment group create --resource-group rg-gifforge-nonprod --template-file infra/main.bicep ...`. Deployments default to `minReplicas=0` and `workerMinReplicas=0` to scale to zero; the worker wakes from the Azure Queue scale rule when generation jobs are waiting.

Deployed environments also default to `generationJobRetentionHours=24`, `temporaryBlobRetentionDays=2`, `retentionCleanupIntervalMinutes=360`, and `retentionCleanupBatchSize=100`. The backend stores an `expiresAt` value with each generation job, returns HTTP `410 Gone` for expired status/result reads, and prunes expired job rows during cleanup passes. Azure Storage lifecycle policy deletes temporary provider result blobs from the private result container. Source media used for retry remains on the client device in the active-generation snapshot and is not stored in backend blob storage.

The `Deploy Nonprod` GitHub Actions workflow can deploy and smoke-test `rg-gifforge-nonprod` manually. It uses resource-group-scope deployment against the existing nonprod resource group. Run `scripts/setup-azure-oidc.sh --environment nonprod` first in dry-run mode to review the Azure OIDC trust, GitHub environment secrets, and resource-group-scoped RBAC changes. After approval, run it with `--apply` to configure `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, the federated credential subject `repo:eslutz/GifForge:environment:nonprod`, and the `Contributor` plus `Role Based Access Control Administrator` roles at the `rg-gifforge-nonprod` scope. Configure `GIFFORGE_APP_ATTEST_APP_IDENTIFIER` and `GIFFORGE_APP_ATTEST_ROOT_CERTIFICATE_PEM` in the `nonprod` GitHub environment, then dispatch the workflow with an immutable backend GHCR commit SHA tag to deploy. Nonprod always deploys with the demo App Attest bypass disabled.

The `Deploy Prod` workflow deploys to `rg-gifforge-prod` through the `prod` GitHub environment. Run `scripts/setup-azure-oidc.sh --environment prod` first, configure the required production App Attest secrets in that GitHub environment, configure provider API keys in Key Vault, and dispatch only with an immutable commit SHA image tag. If prod has legacy suffixed App Attest secrets, recreate their actual values as unsuffixed `GIFFORGE_APP_ATTEST_APP_IDENTIFIER` and `GIFFORGE_APP_ATTEST_ROOT_CERTIFICATE_PEM` secrets in the `prod` environment because GitHub secret values cannot be read back. Production deployment starts the direct video router, disables the demo App Attest bypass, and performs only a `/health` check; generation validation still requires a physical-device App Attest session and the selected provider.

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
GIFFORGE_BACKEND_URL=http://127.0.0.1:8787 \
GIFFORGE_SMOKE_USE_DEMO_APP_ATTEST=true \
scripts/smoke-backend.sh
```

For deployed nonprod, the workflow smoke test checks `/health` and confirms protected generation routes reject unauthenticated requests. Validate end-to-end generation from a physical device through the normal App Attest flow.

## CI

GitHub Actions is split into path-scoped workflows:

- `Backend` runs for `Backend/**`, `Backend.Tests/**`, `.dockerignore`, and backend workflow changes.
- `Infrastructure` runs for `infra/**` and infrastructure workflow changes.
- `Client` runs for `Client/**` and client workflow changes.
- `Deploy Nonprod` is manual-only and deploys the selected backend image to Azure, then smoke-tests the Container Apps URL.
- `Deploy Prod` is manual-only and deploys an immutable backend image tag to Azure production, then health-checks the Container Apps URL.

The client workflow also builds the containing app, Messages extension, and UI test target for iOS Simulator. Documentation-only changes do not run these workflows. Pushes to `main` that touch backend code also authenticate to GHCR and publish the backend image.

## End-to-End Demo Flow

1. Start the local backend with `ASPNETCORE_HTTP_PORTS=8787 dotnet run --project Backend/GifForge.Backend.csproj`.
2. Launch the containing app once and confirm the backend URL in Settings.
3. Open Messages, select the GifForge iMessage app, and enter a prompt.
4. Optionally add an image with the Photos picker.
5. Select a caption mode.
6. Tap Generate.
7. The extension plans a structured request, submits a backend job, polls until completion, downloads a fake frame sequence from the demo provider, renders a local GIF, and shows a preview.
8. Tap Insert to add the GIF to the Messages compose field.
9. Send manually from Messages.
