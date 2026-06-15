# Gifster Azure Infrastructure

This folder contains the Azure Bicep deployment for the production backend target:

- Azure Container Apps consumption workload profile for the ASP.NET Core Minimal API.
- Log Analytics workspace for Container Apps logs.
- User-assigned managed identity for the backend.
- Storage account with private blob containers, queues, and a job-state table.
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

## Backend Runtime Settings

The Container App receives these environment variables:

- `ASPNETCORE_HTTP_PORTS`
- `GIFSTER_PUBLIC_BASE_URL`
- `GIFSTER_STORAGE_ACCOUNT_NAME`
- `GIFSTER_GENERATION_QUEUE_NAME`
- `GIFSTER_PROVIDER_CALLBACK_QUEUE_NAME`
- `GIFSTER_DELETION_QUEUE_NAME`
- `GIFSTER_RESULTS_CONTAINER_NAME`
- `GIFSTER_SOURCE_IMAGES_CONTAINER_NAME`
- `GIFSTER_JOBS_TABLE_NAME`
- `GIFSTER_KEY_VAULT_URI`

Provider credentials should be added to Key Vault after deployment, then read by the backend through managed identity. Do not store provider secrets in Bicep parameter files.
