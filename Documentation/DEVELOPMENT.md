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

## Run Shared Swift Tests

```bash
cd Client/Packages/GifsterCore
swift test --scratch-path /private/tmp/gifster-swiftpm
```

## Run Backend Tests

```bash
dotnet run --project Backend.Tests/Gifster.Backend.Tests.csproj
```

## Run the Local Backend

```bash
ASPNETCORE_HTTP_PORTS=8787 dotnet run --project Backend/Gifster.Backend.csproj
```

The backend listens at `http://127.0.0.1:8787`.

## Backend Deployment Direction

The production backend target is ASP.NET Core Minimal API with Native AOT on Azure Container Apps. Keep the public API thin and stateless, then use Azure Queue Storage for asynchronous provider orchestration, Blob Storage for temporary media/result handoff, and Table Storage or Cosmos DB for durable job state. Store provider credentials in Container Apps secrets or Key Vault and use managed identity for Azure resource access.

Pushes to `main` publish the backend container to GitHub Container Registry as:

- `ghcr.io/eslutz/gifster-backend:latest`
- `ghcr.io/eslutz/gifster-backend:<commit-sha>`

## Validate Infrastructure

```bash
az bicep build --file infra/main.bicep
az bicep build --file infra/main.subscription.bicep
```

Deploy with `az deployment sub create` after setting the `containerImage` parameter to a pushed backend image, preferably the immutable commit SHA tag from GHCR for repeatable deployments. Use `infra/main.subscription.bicep` for normal environment creation; it creates `rg-gifster-nonprod` or `rg-gifster-prod` and then deploys `infra/main.bicep` into that resource group.

## CI

GitHub Actions runs backend build/tests, Native AOT publish, Docker image build, Bicep compilation, XcodeGen regeneration, Swift package tests, and plist linting on pushes and pull requests. Pushes to `main` also authenticate to GHCR and publish the backend image.

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
