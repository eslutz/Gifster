#!/usr/bin/env bash
set -euo pipefail

repo="eslutz/Gifster"
environment="nonprod"
resource_group=""
app_name=""
apply="false"
subscription_id="${AZURE_SUBSCRIPTION_ID:-}"
tenant_id="${AZURE_TENANT_ID:-}"

usage() {
  cat <<'USAGE'
Usage: scripts/setup-azure-oidc.sh [--apply] [options]

Configures a GitHub Actions Azure OIDC identity for a Gifster environment.
Dry-run is the default. Pass --apply to create or update Azure/GitHub state.

Options:
  --apply                         Apply changes. Without this, commands are printed only.
  --repo OWNER/REPO               GitHub repository. Default: eslutz/Gifster.
  --environment NAME              GitHub environment. Allowed: nonprod, prod. Default: nonprod.
  --resource-group NAME           Azure resource group scope. Default: rg-gifster-<environment>.
  --app-name NAME                 Azure app registration display name. Default: Gifster-GitHub-Actions-<environment>.
  --subscription-id ID            Azure subscription id. Defaults to AZURE_SUBSCRIPTION_ID or az account.
  --tenant-id ID                  Azure tenant id. Defaults to AZURE_TENANT_ID or az account.
  -h, --help                      Show this help.

Required tools in --apply mode:
  az, gh

The script intentionally grants roles only at the selected resource-group scope:
  Contributor
  Role Based Access Control Administrator
USAGE
}

require_value() {
  local option="$1"
  local value="${2:-}"
  if [[ -z "$value" || "$value" == --* ]]; then
    printf 'Missing value for %s\n\n' "$option" >&2
    usage >&2
    exit 2
  fi
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --apply)
      apply="true"
      shift
      ;;
    --repo)
      require_value "$1" "${2:-}"
      repo="$2"
      shift 2
      ;;
    --environment)
      require_value "$1" "${2:-}"
      environment="$2"
      shift 2
      ;;
    --resource-group)
      require_value "$1" "${2:-}"
      resource_group="$2"
      shift 2
      ;;
    --app-name)
      require_value "$1" "${2:-}"
      app_name="$2"
      shift 2
      ;;
    --subscription-id)
      require_value "$1" "${2:-}"
      subscription_id="$2"
      shift 2
      ;;
    --tenant-id)
      require_value "$1" "${2:-}"
      tenant_id="$2"
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      printf 'Unknown argument: %s\n\n' "$1" >&2
      usage >&2
      exit 2
      ;;
  esac
done

case "$environment" in
  nonprod|prod)
    ;;
  *)
    printf 'Unsupported environment: %s. Expected nonprod or prod.\n' "$environment" >&2
    exit 2
    ;;
esac

resource_group="${resource_group:-rg-gifster-${environment}}"
app_name="${app_name:-Gifster-GitHub-Actions-${environment}}"

require_tool() {
  local tool="$1"
  if ! command -v "$tool" >/dev/null 2>&1; then
    printf 'Missing required tool: %s\n' "$tool" >&2
    exit 1
  fi
}

run() {
  if [[ "$apply" == "true" ]]; then
    "$@"
  else
    printf '+'
    printf ' %q' "$@"
    printf '\n'
  fi
}

require_tool az
require_tool gh

if [[ "$apply" == "true" ]]; then
  subscription_id="${subscription_id:-$(az account show --query id --output tsv)}"
  tenant_id="${tenant_id:-$(az account show --query tenantId --output tsv)}"
else
  subscription_id="${subscription_id:-'<subscription-id>'}"
  tenant_id="${tenant_id:-'<tenant-id>'}"
fi

scope="/subscriptions/${subscription_id}/resourceGroups/${resource_group}"
subject="repo:${repo}:environment:${environment}"
credential_name="github-${repo//\//-}-${environment}"

printf 'Gifster Azure OIDC setup\n'
printf 'Mode: %s\n' "$([[ "$apply" == "true" ]] && printf apply || printf dry-run)"
printf 'Repository: %s\n' "$repo"
printf 'Environment: %s\n' "$environment"
printf 'Resource group scope: %s\n' "$scope"
printf 'Azure app registration: %s\n' "$app_name"
printf 'OIDC subject: %s\n\n' "$subject"

if [[ "$apply" == "true" ]]; then
  az group show --name "$resource_group" --query id --output tsv >/dev/null

  app_id="$(az ad app list --display-name "$app_name" --query '[0].appId' --output tsv)"
  if [[ -z "$app_id" ]]; then
    app_id="$(az ad app create --display-name "$app_name" --query appId --output tsv)"
  fi

  if ! az ad sp show --id "$app_id" --query id --output tsv >/dev/null 2>&1; then
    az ad sp create --id "$app_id" --query id --output tsv >/dev/null
  fi
  principal_id="$(az ad sp show --id "$app_id" --query id --output tsv)"

  existing_credential="$(
    az ad app federated-credential list \
      --id "$app_id" \
      --query "[?name=='${credential_name}'].name | [0]" \
      --output tsv
  )"
  if [[ -z "$existing_credential" ]]; then
    az ad app federated-credential create \
      --id "$app_id" \
      --parameters "{\"name\":\"${credential_name}\",\"issuer\":\"https://token.actions.githubusercontent.com\",\"subject\":\"${subject}\",\"audiences\":[\"api://AzureADTokenExchange\"],\"description\":\"GitHub Actions OIDC for ${repo} ${environment} deployments\"}" \
      --output none
  fi

  ensure_role_assignment() {
    local role="$1"
    local existing
    existing="$(
      az role assignment list \
        --assignee "$principal_id" \
        --role "$role" \
        --scope "$scope" \
        --query '[0].id' \
        --output tsv
    )"

    if [[ -n "$existing" ]]; then
      printf 'Role assignment already exists: %s at %s\n' "$role" "$scope"
      return
    fi

    run az role assignment create \
      --assignee-object-id "$principal_id" \
      --assignee-principal-type ServicePrincipal \
      --role "$role" \
      --scope "$scope" \
      --output none
  }

  ensure_role_assignment Contributor
  ensure_role_assignment "Role Based Access Control Administrator"

  run gh api --method PUT "repos/${repo}/environments/${environment}" --silent
  run gh secret set AZURE_CLIENT_ID --repo "$repo" --env "$environment" --body "$app_id"
  run gh secret set AZURE_TENANT_ID --repo "$repo" --env "$environment" --body "$tenant_id"
  run gh secret set AZURE_SUBSCRIPTION_ID --repo "$repo" --env "$environment" --body "$subscription_id"

  printf '\nConfigured GitHub OIDC for %s/%s using Azure app id %s.\n' "$repo" "$environment" "$app_id"
else
  cat <<DRYRUN
This is a dry run. To apply the setup, run:

  scripts/setup-azure-oidc.sh --apply \\
    --environment ${environment} \\
    --resource-group ${resource_group} \\
    --app-name ${app_name} \\
    --subscription-id ${subscription_id} \\
    --tenant-id ${tenant_id}

Planned actions:
DRYRUN
  run az group show --name "$resource_group" --query id --output tsv
  run az ad app list --display-name "$app_name" --query '[0].appId' --output tsv
  run az ad app create --display-name "$app_name" --query appId --output tsv
  run az ad sp create --id "<app-id>" --query id --output tsv
  run az ad app federated-credential create \
    --id "<app-id>" \
    --parameters "{\"name\":\"${credential_name}\",\"issuer\":\"https://token.actions.githubusercontent.com\",\"subject\":\"${subject}\",\"audiences\":[\"api://AzureADTokenExchange\"],\"description\":\"GitHub Actions OIDC for ${repo} ${environment} deployments\"}" \
    --output none
  run az role assignment create \
    --assignee-object-id "<service-principal-object-id>" \
    --assignee-principal-type ServicePrincipal \
    --role Contributor \
    --scope "$scope" \
    --output none
  run az role assignment create \
    --assignee-object-id "<service-principal-object-id>" \
    --assignee-principal-type ServicePrincipal \
    --role "Role Based Access Control Administrator" \
    --scope "$scope" \
    --output none
  run gh api --method PUT "repos/${repo}/environments/${environment}" --silent
  run gh secret set AZURE_CLIENT_ID --repo "$repo" --env "$environment" --body "<app-id>"
  run gh secret set AZURE_TENANT_ID --repo "$repo" --env "$environment" --body "$tenant_id"
  run gh secret set AZURE_SUBSCRIPTION_ID --repo "$repo" --env "$environment" --body "$subscription_id"
fi
