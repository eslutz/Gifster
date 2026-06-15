# Gifster

Gifster is an iOS 26.5+ iMessage app extension scaffold for generating custom animated GIFs with AI and inserting the finished GIF into Messages as a normal attachment.

The v1 architecture is deliberately provider-neutral:

- The iOS app and Messages extension never call external AI media providers directly.
- Apple Foundation Models are the local planning layer where available.
- The backend owns moderation, provider credentials, provider-specific translation, job state, and temporary result URLs.
- The app renders visible caption text locally and converts the generated motion result into the final GIF.
- Messages insertion uses attachment insertion only. The user manually sends the message.

## Repository Layout

- `Client` - iOS client workspace containing the XcodeGen project, containing app, Messages extension, generated Xcode project, and shared Swift package.
- `Client/project.yml` - XcodeGen project for the containing iOS app and Messages extension.
- `Client/App/Gifster` - containing app SwiftUI UI for onboarding, privacy, history, and settings.
- `Client/Extensions/GifsterMessages` - iMessage extension UI and attachment insertion flow.
- `Client/Packages/GifsterCore` - shared Swift package for planning models, backend client, image preprocessing, GIF rendering, and history.
- `Backend` - ASP.NET Core Minimal API backend with Native AOT settings, provider abstraction, and job polling endpoints.
- `Backend.Tests` - xUnit backend integration and unit tests.
- `Documentation` - product, architecture, privacy, roadmap, and implementation plan.
- `infra` - Azure Bicep templates for the Container Apps backend environment.
- `scripts` - local and deployment smoke-test helpers.
- `.github/workflows` - CI for backend, infrastructure, and iOS project checks.

## Quick Start

```bash
cd Client
xcodegen generate
cd Packages/GifsterCore
swift test --scratch-path /private/tmp/gifster-swiftpm
cd ../../..
dotnet test Backend.Tests/Gifster.Backend.Tests.csproj
ASPNETCORE_HTTP_PORTS=8787 dotnet run --project Backend/Gifster.Backend.csproj
```

The local backend listens at `http://127.0.0.1:8787` by default. The containing app Settings screen stores the backend URL in the shared app-group defaults used by the Messages extension.

In a second terminal, verify the backend demo flow:

```bash
scripts/smoke-backend.sh
```

Production backend direction: ASP.NET Core Minimal API with Native AOT deployed to Azure Container Apps, with Azure Queue Storage for provider orchestration, Blob Storage for media/result handoff, and Table Storage for durable job state.

## Infrastructure

```bash
az deployment sub create \
  --location eastus \
  --template-file infra/main.subscription.bicep \
  --parameters @infra/main.subscription.parameters.example.json
```

See [infra/README.md](infra/README.md) for the Azure resources and deployment notes.

## Current Toolchain Note

This project is configured for iOS 26.5+ and has been verified with Xcode 26.5 and the iOS 26.5 SDK. The scaffold keeps the Apple Foundation Models integration boundary explicit and provides a deterministic local fallback for development.

## Documentation

- [Product Overview](Documentation/PRODUCT.md)
- [Architecture](Documentation/ARCHITECTURE.md)
- [Backend API](Documentation/API.md)
- [Privacy and Safety](Documentation/PRIVACY_AND_SAFETY.md)
- [Privacy Policy Draft](Documentation/PRIVACY_POLICY.md)
- [Development and Demo Setup](Documentation/DEVELOPMENT.md)
- [App Store Readiness](Documentation/APP_STORE_READINESS.md)
- [App Store Metadata Draft](Documentation/APP_STORE_METADATA.md)
- [App Review Notes Draft](Documentation/APP_REVIEW_NOTES.md)
- [Roadmap](Documentation/ROADMAP.md)
- [Implementation Plan](Documentation/IMPLEMENTATION_PLAN.md)
