#!/usr/bin/env bash
set -euo pipefail

base_url="${GIFSTER_BACKEND_URL:-http://127.0.0.1:8787}"
base_url="${base_url%/}"
timeout_seconds="${GIFSTER_SMOKE_TIMEOUT_SECONDS:-60}"
use_demo_app_attest="${GIFSTER_SMOKE_USE_DEMO_APP_ATTEST:-false}"
session_token="${GIFSTER_APP_ATTEST_SESSION_TOKEN:-}"

json_string_value() {
  local key="$1"
  sed -nE "s/.*\"${key}\"[[:space:]]*:[[:space:]]*\"([^\"]*)\".*/\1/p"
}

curl_request() {
  local method="$1"
  local path="$2"
  local body="${3:-}"
  local -a args=(-sS -w $'\n%{http_code}' -X "$method" "${base_url}${path}")

  if [[ -n "$session_token" ]]; then
    args+=(-H "Authorization: Bearer ${session_token}")
  fi

  if [[ -n "$body" ]]; then
    args+=(-H "Content-Type: application/json" --data "$body")
  fi

  curl "${args[@]}"
}

curl_absolute_request() {
  local method="$1"
  local url="$2"
  local -a args=(-sS -w $'\n%{http_code}' -X "$method" "$url")

  if [[ -n "$session_token" ]]; then
    args+=(-H "Authorization: Bearer ${session_token}")
  fi

  curl "${args[@]}"
}

expect_status() {
  local response="$1"
  local expected="$2"
  local label="$3"
  local status="${response##*$'\n'}"
  local body="${response%$'\n'*}"

  if [[ "$status" != "$expected" ]]; then
    printf '%s failed: expected HTTP %s, got HTTP %s\n%s\n' "$label" "$expected" "$status" "$body" >&2
    exit 1
  fi

  printf '%s' "$body"
}

printf 'Smoke testing Gifster backend at %s\n' "$base_url"

health_response="$(curl_request GET /health)"
health_body="$(expect_status "$health_response" 200 "health")"
if [[ "$health_body" != *'"ok":true'* ]]; then
  printf 'health failed: response did not include ok=true\n%s\n' "$health_body" >&2
  exit 1
fi

if [[ "$use_demo_app_attest" == "true" && -z "$session_token" ]]; then
  challenge_response="$(curl_request POST /v1/app-attest/challenges)"
  challenge_body="$(expect_status "$challenge_response" 200 "app attest challenge")"
  challenge_id="$(printf '%s' "$challenge_body" | json_string_value challengeId)"

  if [[ -z "$challenge_id" ]]; then
    printf 'app attest challenge failed: missing challengeId\n%s\n' "$challenge_body" >&2
    exit 1
  fi

  attestation_body=$(
    printf '{"keyId":"demo-key-id","challengeId":"%s","attestationObject":"demo-attestation-object","clientDataHash":"demo-client-data-hash"}' \
      "$challenge_id"
  )
  attestation_response="$(curl_request POST /v1/app-attest/attestations "$attestation_body")"
  attestation_body="$(expect_status "$attestation_response" 200 "app attest demo session")"
  session_token="$(printf '%s' "$attestation_body" | json_string_value sessionToken)"

  if [[ -z "$session_token" ]]; then
    printf 'app attest demo session failed: missing sessionToken\n%s\n' "$attestation_body" >&2
    exit 1
  fi
fi

generation_request='{
  "id":"smoke-test",
  "mode":"text_to_gif",
  "originalPrompt":"a tiny robot waving from a desk",
  "cleanedPrompt":"a tiny robot waving from a desk",
  "expandedPrompt":"Create a short seamless animated loop of a tiny robot waving from a desk. Do not render readable text.",
  "negativePrompt":"readable text, captions, subtitles, logos, watermarks",
  "caption":{"mode":"none","text":null},
  "sourceImage":null,
  "options":{"width":480,"height":360,"loopSeconds":2,"stylePreset":"expressive-loop","motionIntensity":"medium"},
  "clientTraceId":"backend-smoke"
}'

submit_response="$(curl_request POST /v1/generations "$generation_request")"
submit_body="$(expect_status "$submit_response" 202 "generation submit")"
job_id="$(printf '%s' "$submit_body" | json_string_value jobId)"

if [[ -z "$job_id" ]]; then
  printf 'generation submit failed: missing jobId\n%s\n' "$submit_body" >&2
  exit 1
fi

deadline=$((SECONDS + timeout_seconds))
download_url=""

while (( SECONDS < deadline )); do
  status_response="$(curl_request GET "/v1/generations/${job_id}")"
  status_body="$(expect_status "$status_response" 200 "generation status")"
  status_value="$(printf '%s' "$status_body" | json_string_value status)"

  case "$status_value" in
    succeeded)
      download_url="$(printf '%s' "$status_body" | json_string_value downloadUrl)"
      break
      ;;
    failed)
      printf 'generation failed\n%s\n' "$status_body" >&2
      exit 1
      ;;
    queued|running)
      sleep 1
      ;;
    *)
      printf 'generation returned unknown status\n%s\n' "$status_body" >&2
      exit 1
      ;;
  esac
done

if [[ -z "$download_url" ]]; then
  printf 'generation did not succeed within %s seconds\n' "$timeout_seconds" >&2
  exit 1
fi

if [[ "$download_url" == "$base_url"* ]]; then
  result_path="${download_url#${base_url}}"
  result_response="$(curl_request GET "$result_path")"
elif [[ "$download_url" == http://* || "$download_url" == https://* ]]; then
  result_response="$(curl_absolute_request GET "$download_url")"
else
  result_response="$(curl_request GET "$download_url")"
fi
result_body="$(expect_status "$result_response" 200 "generation result")"

if [[ "$result_body" != *'"format":"frame-sequence-v1"'* ]]; then
  printf 'generation result did not include frame-sequence-v1\n%s\n' "$result_body" >&2
  exit 1
fi

printf 'Gifster backend smoke test passed for job %s\n' "$job_id"
