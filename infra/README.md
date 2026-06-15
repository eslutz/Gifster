# Gifster Azure Infrastructure

This folder contains the Azure Bicep deployment for the production backend target:

- Azure Container Apps consumption workload profile for the ASP.NET Core Minimal API and queue worker.
- Log Analytics workspace for Container Apps logs.
- User-assigned managed identity for the backend.
- Storage account with private blob containers, queues, a job-state table, and an App Attest state table.
- Key Vault for external AI provider credentials.
- RBAC assignments for managed identity access to storage data and Key Vault secrets.

## Environments

Gifster uses two Azure environments:

- `nonprod` in `rg-gifster-nonprod`
- `prod` in `rg-gifster-prod`

## Bootstrap an Environment Resource Group

```bash
az deployment sub create \
  --location eastus \
  --template-file infra/main.subscription.bicep \
  --parameters @infra/main.subscription.parameters.example.json
```

The subscription-scoped template creates `rg-gifster-nonprod` and then deploys the backend resources into that group.

Use this path only when bootstrapping or recreating an environment resource group. The normal GitHub nonprod deployment targets the existing resource group with `infra/main.bicep`, which keeps the GitHub Actions identity scoped to `rg-gifster-nonprod` instead of the whole subscription.

## Deploy Nonprod From a Workstation

```bash
az deployment group create \
  --resource-group rg-gifster-nonprod \
  --template-file infra/main.bicep \
  --parameters @infra/main.parameters.example.json
```

Set `containerImage` to a pushed backend image before deployment. Pushes to `main` publish:

- `ghcr.io/eslutz/gifster-backend:latest`
- `ghcr.io/eslutz/gifster-backend:<commit-sha>`

Prefer the commit SHA tag for repeatable environment deployments. The template expects the image to expose HTTP on port `8080`.

The default deployment uses `minReplicas=0` and `workerMinReplicas=0` so the API and worker can scale to zero and reduce idle cost. The worker has an Azure Queue scale rule that wakes it when generation jobs are waiting. Use `minReplicas=1` or `workerMinReplicas=1` only when you intentionally need warm capacity.

Retention defaults are cost- and privacy-oriented: `generationJobRetentionHours=24`, `temporaryBlobRetentionDays=2`, `retentionCleanupIntervalMinutes=360`, and `retentionCleanupBatchSize=100`. Generation status/result routes return HTTP `410 Gone` after the job expiry time, cleanup passes prune expired job rows from Table Storage, and Azure Storage lifecycle policy deletes temporary provider result and source-image blobs.

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

`scripts/setup-nonprod-oidc.sh` remains as a compatibility wrapper for the nonprod defaults used by the current manual deployment workflow.

The helper creates or reuses an Azure app registration, creates its service principal if needed, creates a federated credential on the Azure app registration for the selected GitHub environment, sets the three GitHub environment secrets, and assigns only resource-group-scoped Azure roles.

Required GitHub environment secrets:

- `AZURE_CLIENT_ID`
- `AZURE_TENANT_ID`
- `AZURE_SUBSCRIPTION_ID`

Federated credential values:

- issuer: `https://token.actions.githubusercontent.com`
- subject: `repo:eslutz/Gifster:environment:<environment>`
- audience: `api://AzureADTokenExchange`

GitHub Actions service principal roles at the selected environment resource-group scope:

- `Contributor`, to create and update the Container Apps, storage account, Key Vault, managed identity, and related resources.
- `Role Based Access Control Administrator`, to create the managed identity role assignments declared by `infra/main.bicep` for Storage data-plane access and Key Vault secret access.

## GitHub Nonprod Deployment

The `Deploy Nonprod` workflow is manually dispatched from GitHub Actions. It uses Azure OIDC login, deploys `infra/main.bicep` into the existing `rg-gifster-nonprod` resource group, captures the API Container Apps FQDN, and runs `scripts/smoke-backend.sh`.

Optional secrets for real App Attest smoke testing:

- `GIFSTER_APP_ATTEST_APP_IDENTIFIER`
- `GIFSTER_APP_ATTEST_ROOT_CERTIFICATE_PEM`
- `GIFSTER_APP_ATTEST_SESSION_TOKEN`

Dispatch inputs:

- `image_tag`: GHCR backend tag to deploy, such as a commit SHA from the backend workflow.
- `location`: Azure region, default `eastus`.
- `enable_demo_app_attest_bypass`: enables the nonprod-only demo App Attest bypass for smoke testing when a real device session token is not available.

The workflow deploys the API and worker with `minReplicas=0` and `workerMinReplicas=0`. The API wakes on HTTP traffic, and the worker wakes from the `generation-jobs` queue scaler so the smoke test can still create and process a queued fake-provider generation job.

## GitHub Prod Deployment

The `Deploy Prod` workflow is manually dispatched from GitHub Actions. It uses the `prod` GitHub environment, deploys `infra/main.bicep` into `rg-gifster-prod`, and health-checks `/health`. It intentionally does not run a fake generation smoke test, because production generation requires a real App Attest session and a selected external provider.

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
- `GIFSTER_APP_ATTEST_APP_IDENTIFIER`
- `GIFSTER_APP_ATTEST_ROOT_CERTIFICATE_PEM`
- `GIFSTER_EXTERNAL_PROVIDER_SUBMIT_URL`
- `GIFSTER_EXTERNAL_PROVIDER_RESULT_URL_TEMPLATE`

Optional `prod` GitHub environment secrets and variables:

- `GIFSTER_EXTERNAL_PROVIDER_AUTHORIZATION`: server-side Authorization header for the external provider gateway.
- `GIFSTER_EXTERNAL_PROVIDER_NAME`: optional GitHub environment variable for health/status display.

Production dispatch rejects `latest` and requires an immutable 40-character commit SHA image tag. It deploys with `providerAdapter=external-http`, `appAttestDemoBypassEnabled=false`, and the selected `minReplicas`, `workerMinReplicas`, and `maxReplicas` values.

## Smoke Test Nonprod

After deployment, run the backend smoke test against the Container Apps URL:

```bash
GIFSTER_BACKEND_URL=https://<api-app-url> scripts/smoke-backend.sh
```

The smoke test checks `/health`, submits a fake-provider generation job, polls status, and downloads the generated frame-sequence result. If App Attest enforcement is enabled without physical-device App Attest material available to the smoke script, use the explicit demo bypass only in controlled nonprod testing:

```bash
GIFSTER_BACKEND_URL=https://<api-app-url> \
GIFSTER_SMOKE_USE_DEMO_APP_ATTEST=true \
scripts/smoke-backend.sh
```

Do not enable the demo App Attest bypass in production.

## Backend Runtime Settings

The API and worker Container Apps receive these environment variables:

- `ASPNETCORE_HTTP_PORTS`
- `AZURE_CLIENT_ID`
- `GIFSTER_APP_ATTEST_REQUIRED`
- `GIFSTER_APP_ATTEST_APP_IDENTIFIER`
- `GIFSTER_APP_ATTEST_ROOT_CERTIFICATE_PEM`
- `GIFSTER_PUBLIC_BASE_URL`
- `GIFSTER_STORAGE_ACCOUNT_NAME`
- `GIFSTER_GENERATION_QUEUE_NAME`
- `GIFSTER_PROVIDER_CALLBACK_QUEUE_NAME`
- `GIFSTER_DELETION_QUEUE_NAME`
- `GIFSTER_RESULTS_CONTAINER_NAME`
- `GIFSTER_SOURCE_IMAGES_CONTAINER_NAME`
- `GIFSTER_JOBS_TABLE_NAME`
- `GIFSTER_APP_ATTEST_STATE_TABLE_NAME`
- `GIFSTER_KEY_VAULT_URI`
- `GIFSTER_PROVIDER_ADAPTER`
- `GIFSTER_EXTERNAL_PROVIDER_NAME`
- `GIFSTER_EXTERNAL_PROVIDER_SUBMIT_URL`
- `GIFSTER_EXTERNAL_PROVIDER_RESULT_URL_TEMPLATE`
- `GIFSTER_EXTERNAL_PROVIDER_AUTHORIZATION`
- `GIFSTER_GENERATION_JOB_RETENTION_HOURS`
- `GIFSTER_RETENTION_CLEANUP_ENABLED`
- `GIFSTER_RETENTION_CLEANUP_INTERVAL_MINUTES`
- `GIFSTER_RETENTION_CLEANUP_BATCH_SIZE`

The worker also sets `GIFSTER_WORKER_ENABLED=true` and processes jobs from the `generation-jobs` queue. Worker baseline availability is controlled by the `workerMinReplicas` deployment parameter; queue depth controls scale-out from zero through the Azure Queue scale rule.

The templates intentionally do not set `GIFSTER_APP_ATTEST_DEMO_BYPASS`. That bypass exists only for local and controlled nonprod smoke testing and must not be enabled in production. Set `appAttestAppIdentifier` and `appAttestRootCertificatePem` before testing real App Attest enforcement.

Set `providerAdapter=external-http` only after a provider gateway or vendor-specific wrapper implements the documented external HTTP provider contract. Keep `providerAdapter=fake` for local/demo deployments.

Provider credentials should be added as Container Apps secrets through the secure `externalProviderAuthorization` deployment parameter or added to Key Vault for provider-specific adapter work. Do not store provider secrets in Bicep parameter files.
