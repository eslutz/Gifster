# GifForge Azure Infrastructure

This folder contains the Azure Bicep deployment for the production backend target:

- Azure Container Apps consumption workload profile for the ASP.NET Core Minimal API and queue worker.
- Log Analytics workspace for Container Apps logs.
- User-assigned managed identity for the backend.
- Storage account with private blob containers, queues, a job-state table, and an App Attest state table.
- Azure App Configuration for provider/model routing settings.
- Key Vault for external AI provider credentials.
- RBAC assignments for managed identity access to storage data, App Configuration, and Key Vault secrets.

## Environments

GifForge uses two Azure environments:

- `nonprod` in `rg-gifforge-nonprod`
- `prod` in `rg-gifforge-prod`

Shared observability lives outside those runtime environments:

- `shared` in `rg-gifforge-shared`

The shared observability stack owns `gifforge-shared-logs`, the account-level AI provider log-drain receiver, and the custom `ProviderLogs_CL` table. `prod` and `nonprod` Container Apps should send application logs to this shared workspace while retaining structured fields for filtering.

Shared observability defaults to `eastus2`. The backend environments currently remain in `eastus`.

## Deploy Shared Observability

The `Deploy Shared Observability` workflow is manually dispatched from GitHub Actions. It uses the `shared` GitHub environment, deploys `infra/shared-observability.subscription.bicep`, and zip-deploys the standalone C# provider log-drain Function.

Required `shared` GitHub environment secrets:

- `AZURE_CLIENT_ID`
- `AZURE_TENANT_ID`
- `AZURE_SUBSCRIPTION_ID`
- `FAL_DRAIN_SECRET`

`FAL_DRAIN_SECRET` must be at least 64 characters. Use the workflow output to configure fal.ai:

- Name: `GifForge Provider Logs`
- Endpoint URL: the workflow's `Fal drain endpoint`
- Secret Token: the exact `FAL_DRAIN_SECRET` value from the `shared` GitHub environment
- Sampling Rate: `1000`

fal.ai currently supports one log drain per account, so the provider drain is shared across GifForge prod and nonprod. Do not infer environment from fal alone. Join provider logs to backend generation logs through `ProviderJobId` when the provider emits a matching job/request id.

The shared deployment discovers `GifForge-GitHub-Actions-nonprod` and `GifForge-GitHub-Actions-prod` service principals and grants them Log Analytics Contributor on `gifforge-shared-logs`. The prod and nonprod deploy workflows need that access to read the shared workspace key while wiring Container Apps logs across resource groups.

The provider-drain Function is intentionally separate from the backend API and worker. It accepts `POST /api/provider-drains/fal`, validates `X-Fal-Signature`, normalizes NDJSON into `ProviderLogs_CL`, and uses Azure Monitor Logs Ingestion API through a managed identity. Future providers should use `POST /api/provider-drains/{providerName}` only after their authentication contract is implemented.

After the shared receiver is verified, delete the temporary nonprod resources tagged `purpose=fal-log-drain`: `gifforge-fal-drain-tspytd5`, `gffaldrain38249`, and the matching Application Insights component.

## Bootstrap an Environment Resource Group

```bash
az deployment sub create \
  --location eastus \
  --template-file infra/main.subscription.bicep \
  --parameters @infra/main.subscription.parameters.example.json
```

The subscription-scoped template creates `rg-gifforge-nonprod` and then deploys the backend resources into that group.

Use this path only when bootstrapping or recreating an environment resource group. The normal GitHub nonprod deployment targets the existing resource group with `infra/main.bicep`, which keeps the GitHub Actions identity scoped to `rg-gifforge-nonprod` instead of the whole subscription.

## Deploy Nonprod From a Workstation

```bash
az deployment group create \
  --resource-group rg-gifforge-nonprod \
  --template-file infra/main.bicep \
  --parameters @infra/main.parameters.example.json
```

Set `containerImage` to a pushed backend image before deployment. Pushes to `main` publish:

- `ghcr.io/eslutz/gifforge-backend:latest`
- `ghcr.io/eslutz/gifforge-backend:<commit-sha>`

For pre-release or feature-branch deployments, manually dispatch the `Backend` workflow from the target branch with `publish_image=true`. That builds, tests, publishes the Native AOT backend image, and pushes `ghcr.io/eslutz/gifforge-backend:<commit-sha>` for the selected ref without requiring a local Docker build.

Prefer the commit SHA tag for repeatable environment deployments. The template expects the image to expose HTTP on port `8080`.

The default deployment uses `minReplicas=0` and `workerMinReplicas=0` so the API and worker can scale to zero and reduce idle cost. The worker has an Azure Queue scale rule that wakes it when generation jobs are waiting. Use `minReplicas=1` or `workerMinReplicas=1` only when you intentionally need warm capacity.

Retention defaults are cost- and privacy-oriented: `generationJobRetentionHours=24`, `temporaryBlobRetentionDays=2`, `retentionCleanupIntervalMinutes=360`, and `retentionCleanupBatchSize=100`. Generation status/result routes return HTTP `410 Gone` after the job expiry time, cleanup passes prune expired job rows from Table Storage, and Azure Storage lifecycle policy deletes temporary provider result blobs. Source media retry material stays on the client device and is not retained in backend blob storage.

## GitHub Environment OIDC Setup

Use the environment-aware setup helper in dry-run mode first:

```bash
scripts/setup-azure-oidc.sh \
  --environment nonprod \
  --subscription-id <subscription-id> \
  --tenant-id <tenant-id>
```

For production, use the same helper with the production GitHub environment and resource group:

```bash
scripts/setup-azure-oidc.sh \
  --environment prod \
  --subscription-id <subscription-id> \
  --tenant-id <tenant-id>
```

After reviewing the planned Azure trust, GitHub secrets, and resource-group-scoped RBAC changes, apply them intentionally:

```bash
scripts/setup-azure-oidc.sh --apply \
  --environment nonprod \
  --subscription-id <subscription-id> \
  --tenant-id <tenant-id>
```

The helper creates or reuses an Azure app registration, creates its service principal if needed, creates a federated credential on the Azure app registration for the selected GitHub environment, sets the three Azure OIDC GitHub environment secrets, and assigns only resource-group-scoped Azure roles. Configure the App Attest environment secrets separately from their secure source values.

After applying setup, audit readiness without changing Azure or GitHub state:

```bash
scripts/audit-azure-oidc-readiness.rb \
  --environment nonprod \
  --strict
```

For production, run the same audit with `--environment prod`. The audit checks the GitHub environment, required secret names, Azure app registration, service principal, federated credential issuer/subject/audience, and the two resource-group-scoped role assignments. Its JSON output is written under ignored `Documentation/DeploymentEvidence/` and records secret names only.

Required GitHub environment secrets:

- `AZURE_CLIENT_ID`
- `AZURE_TENANT_ID`
- `AZURE_SUBSCRIPTION_ID`
- `GIFFORGE_APP_ATTEST_APP_IDENTIFIER`
- `GIFFORGE_APP_ATTEST_ROOT_CERTIFICATE_PEM`

Federated credential values:

- issuer: `https://token.actions.githubusercontent.com`
- subject: `repo:eslutz/GifForge:environment:<environment>`
- audience: `api://AzureADTokenExchange`

GitHub Actions service principal roles at the selected environment resource-group scope:

- `Contributor`, to create and update the Container Apps, storage account, Key Vault, managed identity, and related resources.
- `Role Based Access Control Administrator`, to create the managed identity role assignments declared by `infra/main.bicep` for Storage data-plane access and Key Vault secret access.

## GitHub Nonprod Deployment

The `Deploy Nonprod` workflow is manually dispatched from GitHub Actions. It uses Azure OIDC login, deploys `infra/main.bicep` into the existing `rg-gifforge-nonprod` resource group, points Container Apps logs at `gifforge-shared-logs` in `rg-gifforge-shared`, captures the API Container Apps FQDN, and runs `scripts/smoke-backend.sh`.

Required `nonprod` GitHub environment secrets for App Attest:

- `GIFFORGE_APP_ATTEST_APP_IDENTIFIER`
- `GIFFORGE_APP_ATTEST_ROOT_CERTIFICATE_PEM`

Dispatch inputs:

- `image_tag`: immutable GHCR backend commit SHA tag to deploy.
- `location`: Azure region, default `eastus`.

If the image tag is from a branch that has not merged to `main`, publish it first with the manual `Backend` workflow from that branch.

The workflow deploys the API and worker with `minReplicas=0` and `workerMinReplicas=0`. The API wakes on HTTP traffic, and the worker wakes from the `generation-jobs` queue scaler when authenticated generation jobs are waiting.

After a successful deployment, capture read-only evidence for the release record:

```bash
scripts/collect-deployment-evidence.rb \
  --environment nonprod \
  --workflow-run-id <github-actions-run-id>
```

The collector writes JSON under `Documentation/DeploymentEvidence/` by default. That directory is ignored by git because evidence can include environment-specific URLs, resource names, and run metadata.

## GitHub Prod Deployment

The `Deploy Prod` workflow is manually dispatched from GitHub Actions. It uses the `prod` GitHub environment, deploys `infra/main.bicep` into `rg-gifforge-prod`, points Container Apps logs at `gifforge-shared-logs` in `rg-gifforge-shared`, and health-checks `/health`. It intentionally does not run a generation smoke test, because production generation requires a real App Attest session and configured provider API keys.

Before dispatching production, configure OIDC with:

```bash
scripts/setup-azure-oidc.sh --apply \
  --environment prod \
  --subscription-id <subscription-id> \
  --tenant-id <tenant-id>
```

Required `prod` GitHub environment secrets:

- `AZURE_CLIENT_ID`
- `AZURE_TENANT_ID`
- `AZURE_SUBSCRIPTION_ID`
- `GIFFORGE_APP_ATTEST_APP_IDENTIFIER`
- `GIFFORGE_APP_ATTEST_ROOT_CERTIFICATE_PEM`

If prod has legacy suffixed App Attest secret names, recreate the same actual values under the unsuffixed `GIFFORGE_APP_ATTEST_APP_IDENTIFIER` and `GIFFORGE_APP_ATTEST_ROOT_CERTIFICATE_PEM` names in the `prod` environment before dispatching. GitHub secret values cannot be read back, so use the original secure source for those values.

Provider API keys are not GitHub deployment secrets. Store `GIFFORGE_FAL_API_KEY` and `GIFFORGE_LUMA_API_KEY` in Key Vault, and store non-secret provider enablement/cost values in Azure App Configuration.

Production dispatch rejects `latest` and requires an immutable 40-character commit SHA image tag. Production always starts the direct video router, uses `appAttestDemoBypassEnabled=false`, and applies the selected `minReplicas`, `workerMinReplicas`, and `maxReplicas` values.

Before creating production resources, run a subscription-scope what-if with placeholder provider/App Attest values and the intended image tag:

```bash
az deployment sub what-if \
  --name gifforge-prod-bootstrap-whatif \
  --location eastus \
  --template-file infra/main.subscription.bicep \
  --result-format ResourceIdOnly \
  --parameters \
    environmentName=prod \
    resourceGroupName=rg-gifforge-prod \
    location=eastus \
    containerImage=ghcr.io/eslutz/gifforge-backend:<commit-sha> \
    appAttestAppIdentifier=TEAMID.dev.ericslutz.gifforge \
    appAttestRootCertificatePem=placeholder \
    appAttestDemoBypassEnabled=false \
    minReplicas=0 \
    workerMinReplicas=0 \
    maxReplicas=10
```

The production what-if should show creation of `rg-gifforge-prod`, the API and worker Container Apps, model cost updater Function App, managed environment, Key Vault, App Configuration, managed identities, Log Analytics workspace, Storage account, queues, tables, blob containers, lifecycle policy, and role assignments. Do not run the real production deployment until the `prod` GitHub environment has OIDC secrets, production App Attest values, provider configuration, provider API keys in Key Vault, and an immutable GHCR image tag.

After production deployment, run:

```bash
scripts/collect-deployment-evidence.rb \
  --environment prod \
  --workflow-run-id <github-actions-run-id>
```

Preserve the generated JSON with the App Store release evidence. The collector records only sanitized Container Apps environment variable names, not environment variable values or secret values.

## Smoke Test Nonprod

After deployment, run the backend smoke test against the Container Apps URL:

```bash
GIFFORGE_BACKEND_URL=https://<api-app-url> scripts/smoke-backend.sh
```

The deployed nonprod smoke test checks `/health` and verifies protected generation routes reject unauthenticated requests with HTTP 401. End-to-end generation in nonprod should be validated from a physical device through the normal App Attest flow. Do not enable the demo App Attest bypass in any deployed environment.

## Structured Log Queries

Shared backend and provider logs are filterable by structured fields. Azure resource tags are required for governance, but structured fields are the query source of truth.

Provider drain logs:

```kusto
ProviderLogs_CL
| where ProviderName == "fal"
| where TimeGenerated > ago(1h)
| order by TimeGenerated desc
| take 50
```

Backend Container Apps logs should include `GifForgeEnvironment` and `GifForgeComponent` in every generation operational event:

```kusto
ContainerAppConsoleLogs_CL
| where TimeGenerated > ago(1h)
| where Log_s has "GifForgeEnvironment"
| where Log_s has "GifForgeComponent"
| order by TimeGenerated desc
| take 50
```

Provider/backend correlation uses `ProviderJobId` when the provider emits a matching id:

```kusto
ProviderLogs_CL
| where isnotempty(ProviderJobId)
| join kind=leftouter (
    ContainerAppConsoleLogs_CL
    | where Log_s has "ProviderJobId"
  ) on ProviderJobId
```

The backend sets these required log fields from deployment configuration:

- `GifForgeEnvironment`: `prod`, `nonprod`, or local fallback `local`
- `GifForgeComponent`: `backend-api`, `backend-worker`, or local fallback `backend-test`
- `GifForgeService`: defaults to `GifForge.Backend`
- `GifForgeVersion`: deployed container image or assembly version

## Backend Runtime Settings

The API and worker Container Apps receive these environment variables:

- `ASPNETCORE_HTTP_PORTS`
- `AZURE_CLIENT_ID`
- `GIFFORGE_ENVIRONMENT_NAME`
- `GIFFORGE_LOG_COMPONENT`
- `GIFFORGE_REQUIRE_STRUCTURED_LOG_CONTEXT`
- `GIFFORGE_VERSION`
- `GIFFORGE_APP_ATTEST_REQUIRED`
- `GIFFORGE_APP_ATTEST_APP_IDENTIFIER`
- `GIFFORGE_APP_ATTEST_ROOT_CERTIFICATE_PEM`
- `GIFFORGE_PUBLIC_BASE_URL`
- `GIFFORGE_STORAGE_ACCOUNT_NAME`
- `GIFFORGE_GENERATION_QUEUE_NAME`
- `GIFFORGE_PROVIDER_CALLBACK_QUEUE_NAME`
- `GIFFORGE_DELETION_QUEUE_NAME`
- `GIFFORGE_RESULTS_CONTAINER_NAME`
- `GIFFORGE_JOBS_TABLE_NAME`
- `GIFFORGE_APP_ATTEST_STATE_TABLE_NAME`
- `GIFFORGE_KEY_VAULT_URI`
- `AZURE_KEY_VAULT_ENDPOINT`
- `AZURE_APP_CONFIG_ENDPOINT`
- `GIFFORGE_GENERATION_JOB_RETENTION_HOURS`
- `GIFFORGE_GENERATION_MAX_ATTEMPTS`
- `GIFFORGE_RETENTION_CLEANUP_ENABLED`
- `GIFFORGE_RETENTION_CLEANUP_INTERVAL_MINUTES`
- `GIFFORGE_RETENTION_CLEANUP_BATCH_SIZE`
- `GIFFORGE_PROVIDER_CALLBACK_SECRET` (from App Configuration/Key Vault when provider callbacks are enabled)

The worker also sets `GIFFORGE_WORKER_ENABLED=true` and processes jobs from the `generation-jobs` queue. Worker baseline availability is controlled by the `workerMinReplicas` deployment parameter; queue depth controls scale-out from zero through the Azure Queue scale rule.

The templates set `GIFFORGE_APP_ATTEST_DEMO_BYPASS=false` for deployed environments. The bypass exists only for local development and must not be enabled in nonprod or production. Set `appAttestAppIdentifier` and `appAttestRootCertificatePem` before testing real App Attest enforcement.

The backend always starts the direct AI video router. It reads provider enablement and model cost overrides from Azure App Configuration when `AZURE_APP_CONFIG_ENDPOINT` is present and reads secrets from Key Vault when `AZURE_KEY_VAULT_ENDPOINT` or `GIFFORGE_KEY_VAULT_URI` is present. Use Key Vault for `GIFFORGE_FAL_API_KEY` and `GIFFORGE_LUMA_API_KEY`; do not store provider API keys in parameter files. If `GIFFORGE_FAL_ENABLED` or `GIFFORGE_LUMA_ENABLED` is omitted, that provider is enabled only when its API key exists. Explicitly setting an enabled flag to `true` without the matching API key fails startup.

## Model Cost Updater

`CostUpdater/GifForge.CostUpdater.csproj` builds a timer-triggered Azure Functions app that updates provider model cost overrides in App Configuration. It uses the same App Configuration store and Key Vault as the backend but a separate managed identity. The backend identity has App Configuration Data Reader. The updater identity has App Configuration Data Owner so it can write cost keys, Key Vault Secrets User so provider pricing APIs can authenticate with existing provider credentials when required, plus storage data roles required by the Functions timer host because the storage account uses identity-based access instead of shared keys.

The updater owns only known `GIFFORGE_MODEL_COST_USD_*` keys. It reads provider credentials from Key Vault only when a pricing API requires authentication. It does not write provider enablement flags, provider/model ids, submit/result URL templates, provider credentials, Key Vault secrets, or dynamic keys discovered from a provider response. The default schedule is `0 0 */6 * * *`, controlled by the `modelCostUpdaterSchedule` Bicep parameter. Small price movements below `modelCostUpdaterMinimumDeltaUsd` are ignored to avoid noisy writes. `modelCostUpdaterDryRun` defaults to `true`; when enabled, the updater validates provider pricing and logs proposed App Configuration writes without mutating App Configuration.

Provider pricing adapters are code-reviewed registry changes:

- Add a provider by creating one `ProviderPricingSource`, adding explicit `ProviderModelMapping` rows in `ProviderPricingRegistry`, and adding fixture-backed tests for the response or page shape.
- Remove a provider by removing its mappings from `ProviderPricingRegistry`; existing App Configuration values are left in place unless a separate cleanup script is intentionally reviewed.
- Prefer official machine-readable pricing APIs. The Luma adapter intentionally uses strict scraping and fails closed when the public page shape is ambiguous.

Useful App Configuration keys:

- `GIFFORGE_FAL_SUBMIT_URL_TEMPLATE`
- `GIFFORGE_FAL_RESULT_URL_TEMPLATE`
- `GIFFORGE_FAL_ENABLED`
- `GIFFORGE_MODEL_COST_USD_FAL_WAN22_TEXT_TO_VIDEO`
- `GIFFORGE_MODEL_COST_USD_FAL_WAN22_IMAGE_TO_VIDEO`
- `GIFFORGE_MODEL_COST_USD_FAL_WAN22_VIDEO_TO_VIDEO`
- `GIFFORGE_LUMA_SUBMIT_URL_TEMPLATE`
- `GIFFORGE_LUMA_RESULT_URL_TEMPLATE`
- `GIFFORGE_LUMA_ENABLED`
- `GIFFORGE_MODEL_COST_USD_LUMA_RAY32_TEXT_TO_VIDEO`
- `GIFFORGE_MODEL_COST_USD_LUMA_RAY32_IMAGE_TO_VIDEO`
- `GIFFORGE_MODEL_COST_USD_LUMA_RAY32_VIDEO_TO_VIDEO`
- `OTEL_EXPORTER_OTLP_ENDPOINT`

Provider/model IDs are code-defined in the backend model catalog and should not be stored in App Configuration. Provider credentials belong in Key Vault. Do not store provider secrets in Bicep parameter files.
