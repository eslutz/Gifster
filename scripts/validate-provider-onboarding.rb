#!/usr/bin/env ruby
# frozen_string_literal: true

require "json"
require "optparse"
require "time"

FRAME_SEQUENCE_CONTENT_TYPE = "application/vnd.gifster.frame-sequence+json"
MP4_CONTENT_TYPE = "video/mp4"
REQUIRED_PROD_SECRETS = %w[
  GIFSTER_EXTERNAL_PROVIDER_SUBMIT_URL
  GIFSTER_EXTERNAL_PROVIDER_RESULT_URL_TEMPLATE
].freeze
OPTIONAL_PROD_SECRETS = %w[
  GIFSTER_EXTERNAL_PROVIDER_AUTHORIZATION
].freeze
REQUIRED_TOP_LEVEL_STRINGS = %w[
  providerName
  decisionOwner
  decisionDate
  adapter
  gatewaySubmitUrl
  gatewayResultUrlTemplate
  authenticationLocation
  costModel
  expectedLatency
  rateLimits
  supportContact
  dataRetentionSummary
  moderationSummary
  termsReview
  rollbackPlan
].freeze
REQUIRED_ARRAYS = %w[
  supportedResultContentTypes
  requiredProductionSecrets
  optionalProductionSecrets
].freeze
REQUIRED_CHECKS = {
  "contract" => %w[
    usesExternalHttpAdapter
    supportsTextToAnimation
    supportsImageToAnimation
    returnsProviderJobId
    resultUrlSupportsProviderJobId
    acceptsCaptionModeMetadata
    acceptsRenderCaptionLocallyTrue
    doesNotRequireReadableTextRendering
    supportsMp4OrFrameSequence
    handlesNotReadyResultState
    preflightTextPassed
    preflightImagePassed
  ],
  "securityPrivacy" => %w[
    credentialsBackendOnly
    noIosDirectProviderCalls
    noCaptionTextSentForRendering
    noRawOriginalPromptRequiredByProvider
    providerDataUseReviewed
    providerRetentionReviewed
    dataProcessingTermsReviewed
    moderationPathDefined
    abuseReportingPathDefined
  ],
  "productionReadiness" => %w[
    productionSecretsNamed
    nonprodSmokePlanDefined
    prodDeployPlanDefined
    healthModeExternalExpected
    appAttestRequired
    costLimitDefined
    rateLimitHandlingDefined
    outageFallbackDefined
  ]
}.freeze
FORBIDDEN_KEY_PATTERNS = [
  /secretValue/i,
  /authorizationValue/i,
  /\A(?:apiKey|accessToken|refreshToken|bearerToken|password|token)\z/i
].freeze

options = {
  template: nil
}

OptionParser.new do |parser|
  parser.banner = "Usage: scripts/validate-provider-onboarding.rb [provider-evidence.json] [--template PATH]"

  parser.on("--template PATH", "Write a provider onboarding evidence template and exit.") do |path|
    options[:template] = path
  end

  parser.on("-h", "--help", "Show this help.") do
    puts parser
    exit
  end
end.parse!

def checklist(keys)
  keys.to_h { |key| [key.to_sym, false] }
end

def evidence_template
  {
    collectedAt: Time.now.utc.iso8601,
    providerName: "",
    decisionOwner: "",
    decisionDate: "",
    adapter: "external-http",
    gatewaySubmitUrl: "",
    gatewayResultUrlTemplate: "",
    supportedResultContentTypes: [
      MP4_CONTENT_TYPE,
      FRAME_SEQUENCE_CONTENT_TYPE
    ],
    authenticationLocation: "GitHub environment secret or Azure Container Apps secret; do not store secret values here.",
    requiredProductionSecrets: REQUIRED_PROD_SECRETS,
    optionalProductionSecrets: OPTIONAL_PROD_SECRETS,
    costModel: "",
    expectedLatency: "",
    rateLimits: "",
    supportContact: "",
    dataRetentionSummary: "",
    moderationSummary: "",
    termsReview: "",
    rollbackPlan: "",
    contract: checklist(REQUIRED_CHECKS.fetch("contract")).merge(
      providerPayloadInvariant: "Provider-facing requests include renderCaptionLocally=true and never ask providers to render readable caption text.",
      textPreflightEvidence: "",
      imagePreflightEvidence: "",
      notes: ""
    ),
    securityPrivacy: checklist(REQUIRED_CHECKS.fetch("securityPrivacy")).merge(
      notes: ""
    ),
    productionReadiness: checklist(REQUIRED_CHECKS.fetch("productionReadiness")).merge(
      nonprodSmokeEvidence: "",
      productionDeployEvidence: "",
      notes: ""
    )
  }
end

def validate_no_sensitive_keys(value, path, errors)
  case value
  when Hash
    value.each do |key, child|
      key_path = path.empty? ? key.to_s : "#{path}.#{key}"
      if FORBIDDEN_KEY_PATTERNS.any? { |pattern| key.to_s.match?(pattern) }
        errors << "#{key_path} should not be stored in source-controlled provider evidence. Keep provider credentials in GitHub environment secrets, Azure secrets, or Key Vault."
      end
      validate_no_sensitive_keys(child, key_path, errors)
    end
  when Array
    value.each_with_index { |child, index| validate_no_sensitive_keys(child, "#{path}[#{index}]", errors) }
  end
end

def require_string(hash, key, path, errors)
  value = hash[key]
  return if value.is_a?(String) && !value.strip.empty?

  errors << "#{path}.#{key} must be a non-empty string."
end

def require_true(hash, key, path, errors)
  return if hash[key] == true

  errors << "#{path}.#{key} must be true."
end

def require_array(hash, key, path, errors)
  value = hash[key]
  return value if value.is_a?(Array) && value.any?

  errors << "#{path}.#{key} must be a non-empty array."
  []
end

if options[:template]
  File.write(options[:template], "#{JSON.pretty_generate(evidence_template)}\n")
  puts "Provider onboarding evidence template written to #{options[:template]}"
  exit
end

path = ARGV.fetch(0) do
  warn "Missing provider evidence JSON path. Use --template PATH to create a template."
  exit 2
end

evidence = JSON.parse(File.read(path))
errors = []

validate_no_sensitive_keys(evidence, "", errors)

REQUIRED_TOP_LEVEL_STRINGS.each do |key|
  require_string(evidence, key, "$", errors)
end

REQUIRED_ARRAYS.each do |key|
  require_array(evidence, key, "$", errors)
end

REQUIRED_CHECKS.each do |section, checks|
  value = evidence[section]
  unless value.is_a?(Hash)
    errors << "$.#{section} must be an object."
    next
  end

  checks.each { |check| require_true(value, check, "$.#{section}", errors) }
end

unless evidence["adapter"] == "external-http"
  errors << "$.adapter must be external-http for the first real provider path."
end

result_types = Array(evidence["supportedResultContentTypes"])
unless result_types.include?(MP4_CONTENT_TYPE) || result_types.include?(FRAME_SEQUENCE_CONTENT_TYPE)
  errors << "$.supportedResultContentTypes must include #{MP4_CONTENT_TYPE} or #{FRAME_SEQUENCE_CONTENT_TYPE}."
end

required_secrets = Array(evidence["requiredProductionSecrets"])
REQUIRED_PROD_SECRETS.each do |secret|
  errors << "$.requiredProductionSecrets must include #{secret}." unless required_secrets.include?(secret)
end

optional_secrets = Array(evidence["optionalProductionSecrets"])
OPTIONAL_PROD_SECRETS.each do |secret|
  errors << "$.optionalProductionSecrets should include #{secret}, even when the selected provider gateway does not require Authorization." unless optional_secrets.include?(secret)
end

gateway_result_template = evidence["gatewayResultUrlTemplate"].to_s
unless gateway_result_template.include?("{providerJobId}") || gateway_result_template.include?("{jobId}")
  errors << "$.gatewayResultUrlTemplate must include {providerJobId} or {jobId}."
end

%w[textPreflightEvidence imagePreflightEvidence].each do |key|
  require_string(evidence.fetch("contract", {}), key, "$.contract", errors)
end

%w[nonprodSmokeEvidence productionDeployEvidence].each do |key|
  require_string(evidence.fetch("productionReadiness", {}), key, "$.productionReadiness", errors)
end

if errors.any?
  warn "Provider onboarding validation failed:"
  errors.each { |error| warn "- #{error}" }
  exit 1
end

puts "Provider onboarding validation passed for #{path}."
