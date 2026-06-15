# Provider Onboarding

Gifster stays provider-neutral. The iOS app never calls external AI media providers directly, and the first real provider should integrate through the backend `external-http` adapter or a provider-specific gateway that implements the same contract.

## Required Decision Evidence

Create an ignored provider evidence file before selecting a paid provider:

```bash
scripts/validate-provider-onboarding.rb --template Documentation/ProviderEvidence/first-provider.json
```

Fill the evidence after provider review and preflight validation, then run:

```bash
scripts/validate-provider-onboarding.rb Documentation/ProviderEvidence/first-provider.json
```

`Documentation/ProviderEvidence/` is ignored by git. Do not put provider credentials, Authorization header values, API keys, bearer tokens, passwords, or raw provider secret values in the evidence file.

## Contract Requirements

The selected provider path must prove:

- Backend uses `providerAdapter=external-http`.
- Text-to-animation and image-to-animation are supported.
- Submit returns a non-empty `providerJobId`.
- Result download accepts `{providerJobId}` or `{jobId}` in the URL template.
- Results are `video/mp4` or `application/vnd.gifster.frame-sequence+json`.
- Not-ready result states are retryable instead of being stored as empty assets.
- Provider payload accepts `captionMode` and `renderCaptionLocally=true`.
- Provider does not require visible caption text or readable text rendering.
- `scripts/validate-external-provider-contract.rb` passes for both text-to-GIF and image-to-GIF.

## Security, Privacy, And Cost Requirements

The selected provider path must prove:

- Credentials stay server-side in GitHub environment secrets, Container Apps secrets, or Key Vault.
- The iOS app still has no direct provider calls.
- Caption text is not sent to the provider for rendering.
- Provider data-use, retention, and data-processing terms have been reviewed.
- Moderation and abuse-reporting paths are defined.
- Cost model, rate limits, outage fallback, and production rollback are documented.

Provider onboarding is not complete until the evidence validates and production deployment evidence confirms `/health` reports `mode=external`.
