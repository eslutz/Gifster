#!/usr/bin/env ruby
# frozen_string_literal: true

require "json"
require "optparse"
require "time"

REQUIRED_SECTIONS = %w[
  containingApp
  messagesCompact
  messagesExpanded
  resumeAndJobState
  appAttestPhysicalDevice
  appleDeveloperPortal
  appStoreConnect
].freeze

REQUIRED_TOP_LEVEL_STRINGS = %w[
  build
  gitCommit
  deviceModel
  iosVersion
  tester
].freeze

SECTION_REQUIRED_STRINGS = {
  "backend" => %w[url imageTag appAttestMode providerAdapter],
  "messagesCompact" => %w[prompt jobId screenshot],
  "messagesExpanded" => %w[prompt captionMode jobId screenshot],
  "resumeAndJobState" => %w[originalJobId reopenedJobId screenshot],
  "appAttestPhysicalDevice" => %w[bundleId appAttestEnvironment backendDeployment jobId],
  "appleDeveloperPortal" => %w[teamId containingAppBundleId extensionBundleId appGroupId archiveEvidence],
  "appStoreConnect" => %w[supportUrl privacyUrl screenshotSet]
}.freeze

REQUIRED_CHECKS = {
  "containingApp" => %w[
    launchesToGifForgeTab
    privacyCopyMentionsBackend
    privacyCopyMentionsLocalModels
    historyLoads
    clearHistoryRequiresConfirmation
    settingsBackendUrlEditable
    settingsAppAttestToggleVisible
    noBroadPhotoPrompt
  ],
  "messagesCompact" => %w[
    opensFromMessagesDrawer
    promptEntryVisible
    addImageVisible
    captionModeVisible
    emptyPromptDisablesGenerate
    validPromptStartsBackendJob
    progressVisible
    recentGifsVisibleAfterGeneration
    errorsVisibleAndActionable
    noStickerUiVisible
  ],
  "messagesExpanded" => %w[
    largerPromptEditorVisible
    sourceImagePreviewVisible
    captionSuggestionsVisible
    captionsReviewableSelectableEditable
    explicitCaptionPreserved
    captionEditsRerenderLocally
    finishedGifPreviewVisible
    regenerateCreatesNewBackendJob
    insertAddsGifAttachment
    messagesRequiresManualSend
    noAutoSend
  ],
  "resumeAndJobState" => %w[
    activeJobStarted
    extensionClosedDuringGeneration
    existingJobResumed
    noDuplicateJobCreated
    completedResultRenders
    failedJobCanBeCleared
    backendExpiredJobNotResumed
  ],
  "appAttestPhysicalDevice" => %w[
    backendRequiresAppAttest
    backendAppIdentifierConfigured
    backendRootCertificateConfigured
    expectedAppAttestEnvironment
    challengeReceived
    sessionTokenIssued
    authorizedRoutesSucceed
    unauthorizedRoutesReturn401
    simulatorUnsupportedDocumented
  ],
  "appleDeveloperPortal" => %w[
    signingValidatorPasses
    containingAppBundleIdExists
    extensionBundleIdExistsAndPrefixed
    appGroupEnabledForBoth
    appAttestEnabledWhereRequired
    appGroupMatchesProject
    releaseSigningUsesIntendedTeam
    archiveValidates
  ],
  "appStoreConnect" => %w[
    appInformationFinal
    supportUrlReachable
    privacyUrlReachable
    reviewContactComplete
    screenshotsAttached
    reviewNotesComplete
    privacyAnswersMatchBackend
    noTrackingClaimed
  ]
}.freeze

FORBIDDEN_KEY_PATTERNS = [
  /phone/i,
  /secret/i,
  /\A(?:token|sessionToken|accessToken|refreshToken|authorization)\z/i,
  /authorization/i,
  /password/i
].freeze

options = {
  template: nil
}

OptionParser.new do |parser|
  parser.banner = "Usage: scripts/validate-device-evidence.rb [evidence.json] [--template PATH]"

  parser.on("--template PATH", "Write a device/App Store evidence template and exit.") do |path|
    options[:template] = path
  end

  parser.on("-h", "--help", "Show this help.") do
    puts parser
    exit
  end
end.parse!

def evidence_template
  {
    collectedAt: Time.now.utc.iso8601,
    build: "",
    gitCommit: "",
    deviceModel: "",
    iosVersion: "",
    tester: "",
    backend: {
      url: "",
      imageTag: "",
      appAttestMode: "",
      providerAdapter: ""
    },
    containingApp: checklist(REQUIRED_CHECKS.fetch("containingApp")).merge(
      screenshot: "",
      notes: ""
    ),
    messagesCompact: checklist(REQUIRED_CHECKS.fetch("messagesCompact")).merge(
      prompt: "",
      jobId: "",
      screenshot: "",
      notes: ""
    ),
    messagesExpanded: checklist(REQUIRED_CHECKS.fetch("messagesExpanded")).merge(
      prompt: "",
      sourceImageDescription: "",
      captionMode: "",
      jobId: "",
      screenshot: "",
      notes: ""
    ),
    resumeAndJobState: checklist(REQUIRED_CHECKS.fetch("resumeAndJobState")).merge(
      originalJobId: "",
      reopenedJobId: "",
      screenshot: "",
      notes: ""
    ),
    appAttestPhysicalDevice: checklist(REQUIRED_CHECKS.fetch("appAttestPhysicalDevice")).merge(
      bundleId: "",
      appAttestEnvironment: "",
      backendDeployment: "",
      jobId: "",
      notes: ""
    ),
    appleDeveloperPortal: checklist(REQUIRED_CHECKS.fetch("appleDeveloperPortal")).merge(
      teamId: "",
      containingAppBundleId: "",
      extensionBundleId: "",
      appGroupId: "",
      archiveEvidence: "",
      notes: ""
    ),
    appStoreConnect: checklist(REQUIRED_CHECKS.fetch("appStoreConnect")).merge(
      supportUrl: "",
      privacyUrl: "",
      screenshotSet: "",
      notes: ""
    )
  }
end

def checklist(keys)
  keys.to_h { |key| [key.to_sym, false] }
end

def validate_no_sensitive_keys(value, path, errors)
  case value
  when Hash
    value.each do |key, child|
      key_path = path.empty? ? key.to_s : "#{path}.#{key}"
      if FORBIDDEN_KEY_PATTERNS.any? { |pattern| key.to_s.match?(pattern) }
        errors << "#{key_path} should not be stored in source-controlled evidence. Keep private values in App Store Connect or secret stores."
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

if options[:template]
  File.write(options[:template], "#{JSON.pretty_generate(evidence_template)}\n")
  puts "Device evidence template written to #{options[:template]}"
  exit
end

path = ARGV.fetch(0) do
  warn "Missing evidence JSON path. Use --template PATH to create a template."
  exit 2
end

evidence = JSON.parse(File.read(path))
errors = []

validate_no_sensitive_keys(evidence, "", errors)

REQUIRED_TOP_LEVEL_STRINGS.each do |key|
  require_string(evidence, key, "$", errors)
end

SECTION_REQUIRED_STRINGS.each do |section, keys|
  value = evidence[section]
  unless value.is_a?(Hash)
    errors << "$.#{section} must be an object."
    next
  end

  keys.each { |key| require_string(value, key, "$.#{section}", errors) }
end

REQUIRED_SECTIONS.each do |section|
  value = evidence[section]
  unless value.is_a?(Hash)
    errors << "$.#{section} must be an object."
    next
  end

  REQUIRED_CHECKS.fetch(section).each do |check|
    require_true(value, check, "$.#{section}", errors)
  end
end

backend = evidence["backend"] || {}
if backend["appAttestMode"] && !%w[development production demo-bypass].include?(backend["appAttestMode"])
  errors << "$.backend.appAttestMode must be development, production, or demo-bypass."
end

if backend["providerAdapter"] && backend["providerAdapter"].strip.empty?
  errors << "$.backend.providerAdapter must name the fake or real provider adapter."
end

if errors.any?
  warn "Device evidence validation failed:"
  errors.each { |error| warn "- #{error}" }
  exit 1
end

puts "Device evidence validation passed for #{path}."
