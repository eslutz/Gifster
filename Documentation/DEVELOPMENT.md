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

- `dev.ericslutz.Gifster`
- `dev.ericslutz.Gifster.MessagesExtension`

Update the app-group identifier if your Apple Developer account requires a different prefix.

The XcodeGen project declares the shared App Group and App Attest capability for both targets. `APP_ATTEST_ENVIRONMENT` is `development` for Debug and `production` for Release, so confirm both values are allowed by the Apple Developer portal before archiving.

Validate the source signing configuration before device or archive work:

```bash
scripts/validate-client-signing.rb
```

The Messages extension bundle id must be prefixed by the containing app bundle id, and both targets must share the same App Group. If you intentionally change the production bundle id or App Group, update `Client/project.yml`, regenerate the Xcode project, and keep both entitlement files in sync.

## Run Shared Swift Tests

```bash
cd Client/Packages/GifsterCore
swift test --scratch-path /private/tmp/gifster-swiftpm
```

## Run Backend Tests

```bash
dotnet test Backend.Tests/Gifster.Backend.Tests.csproj
```

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

Deployed environments set `GIFSTER_APP_ATTEST_REQUIRED=true`. Local development leaves it unset unless you are testing the App Attest challenge/session flow.

Real App Attest verification requires:

- `GIFSTER_APP_ATTEST_APP_IDENTIFIER`: Apple Team ID plus bundle id, such as `TEAMID.dev.ericslutz.Gifster`.
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

The `Deploy Nonprod` GitHub Actions workflow can deploy and smoke-test `rg-gifster-nonprod` manually. It uses resource-group-scope deployment against the existing nonprod resource group. Run `scripts/setup-azure-oidc.sh --environment nonprod` first in dry-run mode to review the Azure OIDC trust, GitHub environment secrets, and resource-group-scoped RBAC changes. After approval, run it with `--apply` to configure `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, the federated credential subject `repo:eslutz/Gifster:environment:nonprod`, and the `Contributor` plus `Role Based Access Control Administrator` roles at the `rg-gifster-nonprod` scope. `scripts/setup-nonprod-oidc.sh` is a compatibility wrapper for the same nonprod defaults. Dispatch the workflow with the backend GHCR image tag to deploy. Use its demo App Attest bypass input only for controlled nonprod smoke tests.

The `Deploy Prod` workflow deploys to `rg-gifster-prod` through the `prod` GitHub environment. Run `scripts/setup-azure-oidc.sh --environment prod` first, configure the required production App Attest and external provider secrets in that GitHub environment, and dispatch only with an immutable commit SHA image tag. Production deployment forces `providerAdapter=external-http`, disables the demo App Attest bypass, and performs only a `/health` check; generation validation still requires a physical-device App Attest session and the selected provider.

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
