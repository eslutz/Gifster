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

## Deploy Nonprod

```bash
az deployment sub create \
  --location eastus \
  --template-file infra/main.subscription.bicep \
  --parameters @infra/main.subscription.parameters.example.json
```

The subscription-scoped template creates `rg-gifster-nonprod` and then deploys the backend resources into that group.

Set `containerImage` to a pushed backend image before deployment. Pushes to `main` publish:

- `ghcr.io/eslutz/gifster-backend:latest`
- `ghcr.io/eslutz/gifster-backend:<commit-sha>`

Prefer the commit SHA tag for repeatable environment deployments. The template expects the image to expose HTTP on port `8080`.

The default deployment uses `minReplicas=0` and `workerMinReplicas=0` so the API and worker can scale to zero and reduce idle cost. The worker has an Azure Queue scale rule that wakes it when generation jobs are waiting. Use `minReplicas=1` or `workerMinReplicas=1` only when you intentionally need warm capacity.

## GitHub Nonprod Deployment

The `Deploy Nonprod` workflow is manually dispatched from GitHub Actions. It uses Azure OIDC login, deploys `infra/main.subscription.bicep`, captures the API Container Apps FQDN, and runs `scripts/smoke-backend.sh`.

Required GitHub environment or repository secrets:

- `AZURE_CLIENT_ID`
- `AZURE_TENANT_ID`
- `AZURE_SUBSCRIPTION_ID`

Optional secrets for real App Attest smoke testing:

- `GIFSTER_APP_ATTEST_APP_IDENTIFIER`
- `GIFSTER_APP_ATTEST_ROOT_CERTIFICATE_PEM`
- `GIFSTER_APP_ATTEST_SESSION_TOKEN`

Dispatch inputs:

- `image_tag`: GHCR backend tag to deploy, such as a commit SHA from the backend workflow.
- `location`: Azure region, default `eastus`.
- `enable_demo_app_attest_bypass`: enables the nonprod-only demo App Attest bypass for smoke testing when a real device session token is not available.

The workflow deploys the API and worker with `minReplicas=0` and `workerMinReplicas=0`. The API wakes on HTTP traffic, and the worker wakes from the `generation-jobs` queue scaler so the smoke test can still create and process a queued fake-provider generation job.

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

The worker also sets `GIFSTER_WORKER_ENABLED=true` and processes jobs from the `generation-jobs` queue. Worker baseline availability is controlled by the `workerMinReplicas` deployment parameter; queue depth controls scale-out from zero through the Azure Queue scale rule.

The templates intentionally do not set `GIFSTER_APP_ATTEST_DEMO_BYPASS`. That bypass exists only for local and controlled nonprod smoke testing and must not be enabled in production. Set `appAttestAppIdentifier` and `appAttestRootCertificatePem` before testing real App Attest enforcement.

Set `providerAdapter=external-http` only after a provider gateway or vendor-specific wrapper implements the documented external HTTP provider contract. Keep `providerAdapter=fake` for local/demo deployments.

Provider credentials should be added to Key Vault after deployment, then read by the backend through managed identity. Do not store provider secrets in Bicep parameter files.
