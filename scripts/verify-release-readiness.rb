#!/usr/bin/env ruby
# frozen_string_literal: true

require "json"
require "rexml/document"
require "yaml"

ROOT = File.expand_path("..", __dir__)

PROJECT_PATH = File.join(ROOT, "Client", "project.yml")
PACKAGE_PATH = File.join(ROOT, "Client", "Packages", "GifsterCore", "Package.swift")
APP_ICON_CONTENTS = File.join(ROOT, "Client", "App", "Gifster", "Assets.xcassets", "AppIcon.appiconset", "Contents.json")
MESSAGES_ICON_CONTENTS = File.join(ROOT, "Client", "Extensions", "GifsterMessages", "Assets.xcassets", "iMessage App Icon.stickersiconset", "Contents.json")
MESSAGES_INFO_PLIST = File.join(ROOT, "Client", "Extensions", "GifsterMessages", "Info.plist")
MESSAGES_VIEW_CONTROLLER = File.join(ROOT, "Client", "Extensions", "GifsterMessages", "MessagesViewController.swift")
MESSAGES_APP_VIEW = File.join(ROOT, "Client", "Extensions", "GifsterMessages", "MessagesAppView.swift")
MESSAGES_COMPOSER_MODEL = File.join(ROOT, "Client", "Extensions", "GifsterMessages", "MessagesComposerModel.swift")
GENERATION_MODELS = File.join(ROOT, "Client", "Packages", "GifsterCore", "Sources", "GifsterCore", "Models", "GenerationModels.swift")
BACKEND_CLIENT = File.join(ROOT, "Client", "Packages", "GifsterCore", "Sources", "GifsterCore", "Networking", "BackendClient.swift")
ACTIVE_GENERATION_STORE = File.join(ROOT, "Client", "Packages", "GifsterCore", "Sources", "GifsterCore", "Storage", "ActiveGenerationStore.swift")
MAIN_BICEP = File.join(ROOT, "infra", "main.bicep")
SUBSCRIPTION_BICEP = File.join(ROOT, "infra", "main.subscription.bicep")
DEPLOY_NONPROD_WORKFLOW = File.join(ROOT, ".github", "workflows", "deploy-nonprod.yml")
DEPLOY_PROD_WORKFLOW = File.join(ROOT, ".github", "workflows", "deploy-prod.yml")

DOCS_WITH_RELEASE_COPY = [
  "Documentation/APP_STORE_METADATA.md",
  "Documentation/APP_REVIEW_NOTES.md",
  "Documentation/APP_STORE_READINESS.md",
  "Documentation/PRIVACY_AND_SAFETY.md",
  "Documentation/PRIVACY_POLICY.md"
].freeze

errors = []

def relative(path)
  path.delete_prefix("#{ROOT}/")
end

def png_dimensions(path)
  bytes = File.binread(path, 24)
  signature = "\x89PNG\r\n\x1a\n".b
  raise "#{relative(path)} is not a PNG file." unless bytes.start_with?(signature)

  width, height = bytes.byteslice(16, 8).unpack("NN")
  [width, height]
end

def declared_dimensions(image)
  scale = image.fetch("scale", "1x").delete_suffix("x").to_i
  width, height = image.fetch("size").split("x").map(&:to_f)
  [(width * scale).round, (height * scale).round]
end

def validate_icon_catalog(contents_path, errors)
  catalog_dir = File.dirname(contents_path)
  contents = JSON.parse(File.read(contents_path))
  images = contents.fetch("images")

  images.each do |image|
    label = "#{relative(contents_path)} #{image.fetch("idiom")} #{image.fetch("size")} #{image.fetch("scale", "1x")}"
    filename = image["filename"]

    if filename.nil? || filename.empty?
      errors << "#{label} is missing a filename."
      next
    end

    png_path = File.join(catalog_dir, filename)
    unless File.file?(png_path)
      errors << "#{label} references missing file #{relative(png_path)}."
      next
    end

    expected = declared_dimensions(image)
    actual = png_dimensions(png_path)
    next if expected == actual

    errors << "#{relative(png_path)} is #{actual.join("x")}, expected #{expected.join("x")}."
  rescue JSON::ParserError => e
    errors << "#{relative(contents_path)} is invalid JSON: #{e.message}."
    break
  rescue KeyError => e
    errors << "#{label || relative(contents_path)} is missing #{e.key.inspect}."
  rescue StandardError => e
    errors << e.message
  end
end

def parse_plist_value(element)
  case element.name
  when "dict"
    values = {}
    children = element.elements.to_a
    index = 0

    while index < children.length
      key = children[index]
      value = children[index + 1]
      index += 2

      next unless key&.name == "key" && value

      values[key.text] = parse_plist_value(value)
    end

    values
  when "array"
    element.elements.map { |child| parse_plist_value(child) }
  when "string"
    element.text.to_s
  when "true"
    true
  when "false"
    false
  else
    element.text.to_s
  end
end

def parse_plist(path)
  document = REXML::Document.new(File.read(path))
  dict = document.root.elements["dict"]
  raise "#{relative(path)} is missing a top-level dict." unless dict

  parse_plist_value(dict)
rescue REXML::ParseException => e
  raise "#{relative(path)} is invalid plist XML: #{e.message}"
end

def validate_messages_extension_metadata(project, errors)
  target = project.fetch("targets").fetch("GifsterMessagesExtension")
  target_type = target.fetch("type")
  errors << "GifsterMessagesExtension target must be app-extension.messages, found #{target_type.inspect}." unless target_type == "app-extension.messages"

  settings = target.fetch("settings").fetch("base")
  extension_api_only = settings.fetch("APPLICATION_EXTENSION_API_ONLY", nil)
  unless [true, "YES"].include?(extension_api_only)
    errors << "GifsterMessagesExtension must set APPLICATION_EXTENSION_API_ONLY to YES, found #{extension_api_only.inspect}."
  end

  info = parse_plist(MESSAGES_INFO_PLIST)
  extension_info = info.fetch("NSExtension", {})
  point_identifier = extension_info["NSExtensionPointIdentifier"]
  principal_class = extension_info["NSExtensionPrincipalClass"]

  unless point_identifier == "com.apple.message-payload-provider"
    errors << "#{relative(MESSAGES_INFO_PLIST)} must use com.apple.message-payload-provider, found #{point_identifier.inspect}."
  end

  unless principal_class == "$(PRODUCT_MODULE_NAME).MessagesViewController"
    errors << "#{relative(MESSAGES_INFO_PLIST)} must use MessagesViewController as the extension principal class, found #{principal_class.inspect}."
  end

  controller_source = File.read(MESSAGES_VIEW_CONTROLLER)
  errors << "#{relative(MESSAGES_VIEW_CONTROLLER)} must import Messages." unless controller_source.match?(/^import Messages$/)
  unless controller_source.match?(/final class MessagesViewController:\s*MSMessagesAppViewController/)
    errors << "#{relative(MESSAGES_VIEW_CONTROLLER)} must subclass MSMessagesAppViewController."
  end
end

def method_body(source, method_name)
  match = source.match(/func #{Regexp.escape(method_name)}\([^)]*\)\s*\{/)
  return nil unless match

  index = match.end(0)
  depth = 1

  while index < source.length
    case source[index]
    when "{"
      depth += 1
    when "}"
      depth -= 1
      return source[match.begin(0)..index] if depth.zero?
    end

    index += 1
  end

  nil
end

def validate_local_caption_rerender(errors)
  view_source = File.read(MESSAGES_APP_VIEW)
  model_source = File.read(MESSAGES_COMPOSER_MODEL)
  apply_body = method_body(model_source, "applyCaptionEdit")

  errors << "#{relative(MESSAGES_APP_VIEW)} must expose an Apply Caption command for local caption edits." unless view_source.include?("model.applyCaptionEdit()")
  errors << "#{relative(MESSAGES_COMPOSER_MODEL)} must cache the downloaded motion asset for caption re-rendering." unless model_source.include?("lastMotionAsset = asset")

  if apply_body.nil?
    errors << "#{relative(MESSAGES_COMPOSER_MODEL)} must implement applyCaptionEdit for local caption re-rendering."
  elsif apply_body.include?("createJob(") || apply_body.include?("JobPollingService(")
    errors << "#{relative(MESSAGES_COMPOSER_MODEL)} applyCaptionEdit must not create or poll a backend generation job."
  end
end

def validate_backend_expiration_contract(errors)
  generation_models = File.read(GENERATION_MODELS)
  backend_client = File.read(BACKEND_CLIENT)
  active_store = File.read(ACTIVE_GENERATION_STORE)

  errors << "#{relative(GENERATION_MODELS)} GenerationJob must preserve backend expiresAt." unless generation_models.include?("public var expiresAt: String?")
  errors << "#{relative(GENERATION_MODELS)} JobSubmissionResponse must decode backend expiresAt." unless generation_models.match?(/struct JobSubmissionResponse[\s\S]*public var expiresAt: String/)
  errors << "#{relative(GENERATION_MODELS)} JobStatusResponse must decode backend expiresAt." unless generation_models.match?(/struct JobStatusResponse[\s\S]*public var expiresAt: String/)
  errors << "#{relative(BACKEND_CLIENT)} createJob must copy response.expiresAt into GenerationJob." unless backend_client.include?("expiresAt: response.expiresAt")
  errors << "#{relative(ACTIVE_GENERATION_STORE)} must clear snapshots whose backend job expiration has passed." unless active_store.include?("snapshot.job.expirationDate")
end

def require_text_match(text, pattern, label, errors)
  errors << "#{label} must match #{pattern.inspect}." unless text.match?(pattern)
end

def require_text_include(text, needle, label, errors)
  errors << "#{label} must include #{needle.inspect}." unless text.include?(needle)
end

def load_workflow(path, errors)
  YAML.load_file(path)
rescue Psych::SyntaxError => e
  errors << "#{relative(path)} is invalid YAML: #{e.message}."
  nil
end

def workflow_dispatch_inputs(workflow, path, errors)
  workflow_on = workflow["on"] || workflow[true]
  inputs = workflow_on&.dig("workflow_dispatch", "inputs")
  return inputs if inputs.is_a?(Hash)

  errors << "#{relative(path)} must define workflow_dispatch inputs."
  nil
end

def workflow_step(workflow, path, job_name, step_name, errors)
  job = workflow.fetch("jobs").fetch(job_name)
  step = job.fetch("steps").find { |candidate| candidate["name"] == step_name }
  return step if step

  errors << "#{relative(path)} job #{job_name.inspect} must define step #{step_name.inspect}."
  nil
rescue KeyError => e
  errors << "#{relative(path)} is missing workflow key #{e.key.inspect}."
  nil
end

def validate_deployment_safety_invariants(errors)
  main_bicep = File.read(MAIN_BICEP)
  subscription_bicep = File.read(SUBSCRIPTION_BICEP)

  require_text_match(main_bicep, /^param minReplicas int = 0$/, "#{relative(MAIN_BICEP)} API scale-to-zero default", errors)
  require_text_match(main_bicep, /^param workerMinReplicas int = 0$/, "#{relative(MAIN_BICEP)} worker scale-to-zero default", errors)
  require_text_match(subscription_bicep, /^param minReplicas int = 0$/, "#{relative(SUBSCRIPTION_BICEP)} API scale-to-zero default", errors)
  require_text_match(subscription_bicep, /^param workerMinReplicas int = 0$/, "#{relative(SUBSCRIPTION_BICEP)} worker scale-to-zero default", errors)
  require_text_include(main_bicep, "appAttestDemoBypassEnabled && environmentName != 'prod' ? 'true' : 'false'", "#{relative(MAIN_BICEP)} production demo-bypass guard", errors)

  nonprod_workflow = load_workflow(DEPLOY_NONPROD_WORKFLOW, errors)
  if nonprod_workflow
    nonprod_deploy = workflow_step(nonprod_workflow, DEPLOY_NONPROD_WORKFLOW, "deploy", "Deploy nonprod infrastructure", errors)
    if nonprod_deploy
      run_script = nonprod_deploy.fetch("run", "")
      require_text_include(run_script, "providerAdapter=fake", "#{relative(DEPLOY_NONPROD_WORKFLOW)} nonprod provider adapter", errors)
      require_text_include(run_script, "minReplicas=0", "#{relative(DEPLOY_NONPROD_WORKFLOW)} nonprod API min replicas", errors)
      require_text_include(run_script, "workerMinReplicas=0", "#{relative(DEPLOY_NONPROD_WORKFLOW)} nonprod worker min replicas", errors)
    end

    smoke_step = workflow_step(nonprod_workflow, DEPLOY_NONPROD_WORKFLOW, "deploy", "Smoke test backend", errors)
    smoke_env = smoke_step&.fetch("env", {}) || {}
    unless smoke_env["GIFSTER_SMOKE_USE_DEMO_APP_ATTEST"] == "${{ inputs.enable_demo_app_attest_bypass }}"
      errors << "#{relative(DEPLOY_NONPROD_WORKFLOW)} smoke test must bind demo App Attest bypass to the manual workflow input."
    end
  end

  prod_workflow = load_workflow(DEPLOY_PROD_WORKFLOW, errors)
  return unless prod_workflow

  inputs = workflow_dispatch_inputs(prod_workflow, DEPLOY_PROD_WORKFLOW, errors)
  if inputs
    if inputs.dig("image_tag", "default")
      errors << "#{relative(DEPLOY_PROD_WORKFLOW)} image_tag must not default to a mutable tag."
    end

    {
      "min_replicas" => "0",
      "worker_min_replicas" => "0",
      "max_replicas" => "10"
    }.each do |input_name, expected_default|
      actual_default = inputs.dig(input_name, "default")
      next if actual_default == expected_default

      errors << "#{relative(DEPLOY_PROD_WORKFLOW)} #{input_name} default must be #{expected_default.inspect}, found #{actual_default.inspect}."
    end
  end

  prod_validate = workflow_step(prod_workflow, DEPLOY_PROD_WORKFLOW, "deploy", "Validate production deployment inputs", errors)
  if prod_validate
    run_script = prod_validate.fetch("run", "")
    require_text_include(run_script, "^[0-9a-f]{40}$", "#{relative(DEPLOY_PROD_WORKFLOW)} immutable image-tag guard", errors)
    %w[
      AZURE_CLIENT_ID
      AZURE_TENANT_ID
      AZURE_SUBSCRIPTION_ID
      GIFSTER_APP_ATTEST_APP_IDENTIFIER
      GIFSTER_APP_ATTEST_ROOT_CERTIFICATE_PEM
      GIFSTER_EXTERNAL_PROVIDER_SUBMIT_URL
      GIFSTER_EXTERNAL_PROVIDER_RESULT_URL_TEMPLATE
    ].each do |secret_name|
      require_text_include(run_script, secret_name, "#{relative(DEPLOY_PROD_WORKFLOW)} required prod secret validation", errors)
    end
  end

  prod_deploy = workflow_step(prod_workflow, DEPLOY_PROD_WORKFLOW, "deploy", "Deploy prod infrastructure", errors)
  return unless prod_deploy

  run_script = prod_deploy.fetch("run", "")
  require_text_include(run_script, "appAttestDemoBypassEnabled=false", "#{relative(DEPLOY_PROD_WORKFLOW)} production demo-bypass disablement", errors)
  require_text_include(run_script, "providerAdapter=external-http", "#{relative(DEPLOY_PROD_WORKFLOW)} production external provider adapter", errors)
  require_text_include(run_script, 'minReplicas="${MIN_REPLICAS}"', "#{relative(DEPLOY_PROD_WORKFLOW)} production API min replicas input", errors)
  require_text_include(run_script, 'workerMinReplicas="${WORKER_MIN_REPLICAS}"', "#{relative(DEPLOY_PROD_WORKFLOW)} production worker min replicas input", errors)
end

project = YAML.load_file(PROJECT_PATH)
deployment_target = project.dig("options", "deploymentTarget", "iOS")
iphoneos_target = project.dig("settings", "base", "IPHONEOS_DEPLOYMENT_TARGET")

errors << "Client/project.yml options.deploymentTarget.iOS must be 26.5, found #{deployment_target.inspect}." unless deployment_target == "26.5"
errors << "Client/project.yml IPHONEOS_DEPLOYMENT_TARGET must be 26.5, found #{iphoneos_target.inspect}." unless iphoneos_target == "26.5"

package_swift = File.read(PACKAGE_PATH)
errors << "GifsterCore Package.swift must declare .iOS(\"26.5\")." unless package_swift.include?(".iOS(\"26.5\")")

source_files = Dir.glob(File.join(ROOT, "Client", "**", "*.swift"))
forbidden_source_patterns = {
  /\bMSSticker\b/ => "v1 must not use sticker APIs.",
  /\bMSStickerBrowserViewController\b/ => "v1 must not use sticker browser APIs.",
  /\binsertSticker\b/ => "v1 must not use sticker insertion.",
  /\bImagePlayground\b/ => "Image Playground must remain out of the main v1 flow."
}

source_files.each do |path|
  text = File.read(path)
  forbidden_source_patterns.each do |pattern, message|
    next unless text.match?(pattern)

    errors << "#{relative(path)} matches #{pattern.inspect}: #{message}"
  end
end

DOCS_WITH_RELEASE_COPY.each do |relative_path|
  path = File.join(ROOT, relative_path)
  unless File.file?(path)
    errors << "#{relative_path} is missing."
    next
  end

  text = File.read(path)
  [
    "PromptGIF",
    "Support URL: TODO",
    "Privacy Policy URL: TODO",
    "Add public",
    "Replace all App Store metadata"
  ].each do |placeholder|
    errors << "#{relative_path} still contains placeholder text #{placeholder.inspect}." if text.include?(placeholder)
  end
end

validate_icon_catalog(APP_ICON_CONTENTS, errors)
validate_icon_catalog(MESSAGES_ICON_CONTENTS, errors)
validate_messages_extension_metadata(project, errors)
validate_local_caption_rerender(errors)
validate_backend_expiration_contract(errors)
validate_deployment_safety_invariants(errors)

if errors.any?
  warn "Release readiness validation failed:"
  errors.each { |error| warn "- #{error}" }
  exit 1
end

puts "Release readiness validation passed."
puts "iOS target: 26.5"
puts "Checked Swift sources for v1 no-sticker/no-Image-Playground invariants."
puts "Checked App Store/review/privacy docs for known placeholders."
puts "Checked app and Messages icon catalogs."
puts "Checked iMessage extension metadata for attachment-insertion app mode."
puts "Checked caption edits can re-render locally without another backend generation job."
puts "Checked client preserves backend generation expiration for active-job resume."
puts "Checked deployment scale-to-zero and production safety invariants."
