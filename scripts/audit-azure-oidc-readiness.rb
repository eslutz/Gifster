#!/usr/bin/env ruby
# frozen_string_literal: true

require "fileutils"
require "json"
require "open3"
require "optparse"
require "time"

ROOT = File.expand_path("..", __dir__)
DEFAULT_REPO = "eslutz/GifForge"
AZURE_OIDC_ISSUER = "https://token.actions.githubusercontent.com"
AZURE_OIDC_AUDIENCE = "api://AzureADTokenExchange"
BASE_REQUIRED_SECRETS = %w[
  AZURE_CLIENT_ID
  AZURE_TENANT_ID
  AZURE_SUBSCRIPTION_ID
  GIFFORGE_APP_ATTEST_APP_IDENTIFIER
  GIFFORGE_APP_ATTEST_ROOT_CERTIFICATE_PEM
].freeze
PROD_REQUIRED_SECRETS = %w[
  GIFFORGE_EXTERNAL_PROVIDER_SUBMIT_URL
  GIFFORGE_EXTERNAL_PROVIDER_RESULT_URL_TEMPLATE
].freeze
PROD_OPTIONAL_SECRET_WARNINGS = %w[
  GIFFORGE_EXTERNAL_PROVIDER_AUTHORIZATION
].freeze
REQUIRED_RESOURCE_GROUP_ROLES = [
  "Contributor",
  "Role Based Access Control Administrator"
].freeze

options = {
  environment: "nonprod",
  repo: DEFAULT_REPO,
  resource_group: nil,
  app_name: nil,
  subscription_id: ENV["AZURE_SUBSCRIPTION_ID"],
  tenant_id: ENV["AZURE_TENANT_ID"],
  output: nil,
  strict: false
}

OptionParser.new do |parser|
  parser.banner = "Usage: scripts/audit-azure-oidc-readiness.rb [options]"

  parser.on("--environment NAME", "GitHub/Azure environment: nonprod or prod. Default: nonprod.") do |value|
    options[:environment] = value
  end

  parser.on("--repo OWNER/REPO", "GitHub repository. Default: #{DEFAULT_REPO}.") do |value|
    options[:repo] = value
  end

  parser.on("--resource-group NAME", "Azure resource group. Default: rg-gifforge-<environment>.") do |value|
    options[:resource_group] = value
  end

  parser.on("--app-name NAME", "Azure app registration display name. Default: GifForge-GitHub-Actions-<environment>.") do |value|
    options[:app_name] = value
  end

  parser.on("--subscription-id ID", "Expected Azure subscription id. Defaults to AZURE_SUBSCRIPTION_ID or az account.") do |value|
    options[:subscription_id] = value
  end

  parser.on("--tenant-id ID", "Expected Azure tenant id. Defaults to AZURE_TENANT_ID or az account.") do |value|
    options[:tenant_id] = value
  end

  parser.on("--output PATH", "JSON output path. Default: Documentation/DeploymentEvidence/<env>-oidc-<timestamp>.json.") do |value|
    options[:output] = value
  end

  parser.on("--strict", "Exit 1 when any required OIDC readiness check fails.") do
    options[:strict] = true
  end

  parser.on("-h", "--help", "Show this help.") do
    puts parser
    exit
  end
end.parse!

unless %w[nonprod prod].include?(options[:environment])
  warn "Unsupported environment #{options[:environment].inspect}. Expected nonprod or prod."
  exit 2
end

options[:resource_group] ||= "rg-gifforge-#{options[:environment]}"
options[:app_name] ||= "GifForge-GitHub-Actions-#{options[:environment]}"
timestamp = Time.now.utc.strftime("%Y%m%dT%H%M%SZ")
options[:output] ||= File.join(
  ROOT,
  "Documentation",
  "DeploymentEvidence",
  "#{options[:environment]}-oidc-#{timestamp}.json"
)

def command_available?(name)
  ENV.fetch("PATH", "").split(File::PATH_SEPARATOR).any? do |directory|
    path = File.join(directory, name)
    File.file?(path) && File.executable?(path)
  end
end

def capture_json(command)
  output, status = Open3.capture2e(*command)
  return [JSON.parse(output), nil] if status.success? && !output.strip.empty?
  return [nil, nil] if status.success?

  [nil, output.strip.empty? ? "#{command.join(" ")} failed" : output.strip]
rescue JSON::ParserError => e
  [nil, "#{command.join(" ")} returned invalid JSON: #{e.message}"]
end

def capture_text(command)
  output, status = Open3.capture2e(*command)
  return [output, nil] if status.success?

  [nil, output.strip.empty? ? "#{command.join(" ")} failed" : output.strip]
end

def add_check(evidence, name, status, detail = nil)
  entry = { name: name, status: status }
  entry[:detail] = detail if detail && !detail.to_s.empty?
  evidence[:checks] << entry
end

def required_secrets_for(environment)
  return BASE_REQUIRED_SECRETS unless environment == "prod"

  BASE_REQUIRED_SECRETS + PROD_REQUIRED_SECRETS
end

def parse_secret_names(output)
  output.to_s.lines.map do |line|
    line.strip.split(/\s+/).first
  end.compact.uniq.sort
end

def role_assignment_present?(assignments, role, scope)
  Array(assignments).any? do |assignment|
    assignment["roleDefinitionName"] == role && assignment["scope"].to_s.casecmp(scope).zero?
  end
end

subject = "repo:#{options[:repo]}:environment:#{options[:environment]}"
credential_name = "github-#{options[:repo].tr("/", "-")}-#{options[:environment]}"
subscription_id = options[:subscription_id]
tenant_id = options[:tenant_id]
scope = "/subscriptions/#{subscription_id || "<subscription-id>"}/resourceGroups/#{options[:resource_group]}"

evidence = {
  collectedAt: Time.now.utc.iso8601,
  environment: options[:environment],
  repository: options[:repo],
  resourceGroup: options[:resource_group],
  azureAppRegistration: options[:app_name],
  expectedSubject: subject,
  expectedFederatedCredentialName: credential_name,
  expectedIssuer: AZURE_OIDC_ISSUER,
  expectedAudience: AZURE_OIDC_AUDIENCE,
  expectedResourceGroupScope: scope,
  requiredGitHubEnvironmentSecrets: required_secrets_for(options[:environment]),
  optionalGitHubEnvironmentSecrets: options[:environment] == "prod" ? PROD_OPTIONAL_SECRET_WARNINGS : [],
  checks: []
}

%w[gh az].each do |tool|
  add_check(evidence, "tool.#{tool}", command_available?(tool) ? "pass" : "fail", "#{tool} must be installed and authenticated.")
end

if evidence[:checks].any? { |check| check[:status] == "fail" }
  FileUtils.mkdir_p(File.dirname(options[:output]))
  File.write(options[:output], "#{JSON.pretty_generate(evidence)}\n")
  warn "OIDC readiness audit could not run because required tools are missing. Evidence written to #{options[:output]}."
  exit(options[:strict] ? 1 : 0)
end

azure_account, azure_account_error = capture_json(
  %w[az account show --query] + ["{subscriptionId:id,tenantId:tenantId,name:name,user:user.name}"] + %w[--output json]
)
if azure_account
  evidence[:azureAccount] = azure_account
  subscription_id ||= azure_account["subscriptionId"]
  tenant_id ||= azure_account["tenantId"]
  scope = "/subscriptions/#{subscription_id}/resourceGroups/#{options[:resource_group]}"
  evidence[:expectedResourceGroupScope] = scope
  add_check(evidence, "azure.account", "pass", "Authenticated as #{azure_account["user"]}.")
else
  add_check(evidence, "azure.account", "fail", azure_account_error)
end

if options[:subscription_id] && azure_account && azure_account["subscriptionId"] != options[:subscription_id]
  add_check(evidence, "azure.subscription", "fail", "Expected #{options[:subscription_id]}, got #{azure_account["subscriptionId"]}.")
elsif subscription_id
  add_check(evidence, "azure.subscription", "pass", subscription_id)
else
  add_check(evidence, "azure.subscription", "fail", "Subscription id is unavailable.")
end

if options[:tenant_id] && azure_account && azure_account["tenantId"] != options[:tenant_id]
  add_check(evidence, "azure.tenant", "fail", "Expected #{options[:tenant_id]}, got #{azure_account["tenantId"]}.")
elsif tenant_id
  add_check(evidence, "azure.tenant", "pass", tenant_id)
else
  add_check(evidence, "azure.tenant", "fail", "Tenant id is unavailable.")
end

github_environment, github_environment_error = capture_json(
  ["gh", "api", "repos/#{options[:repo]}/environments/#{options[:environment]}"]
)
if github_environment
  evidence[:githubEnvironment] = {
    name: github_environment["name"],
    protectionRules: github_environment["protection_rules"]
  }
  add_check(evidence, "github.environment", "pass", options[:environment])
else
  add_check(evidence, "github.environment", "fail", github_environment_error)
end

secret_output, secret_error = capture_text(
  ["gh", "secret", "list", "--repo", options[:repo], "--env", options[:environment]]
)
if secret_output
  secret_names = parse_secret_names(secret_output)
  evidence[:githubEnvironmentSecretNames] = secret_names
  required_secrets_for(options[:environment]).each do |name|
    add_check(evidence, "github.secret.#{name}", secret_names.include?(name) ? "pass" : "fail")
  end

  PROD_OPTIONAL_SECRET_WARNINGS.each do |name|
    next unless options[:environment] == "prod"

    add_check(evidence, "github.optionalSecret.#{name}", secret_names.include?(name) ? "pass" : "warn", "Required only when the provider gateway uses Authorization.")
  end
else
  add_check(evidence, "github.secrets", "fail", secret_error)
end

resource_group, resource_group_error = capture_json(
  ["az", "group", "show", "--name", options[:resource_group], "--query", "{id:id,name:name,location:location,tags:tags}", "--output", "json"]
)
if resource_group
  evidence[:azureResourceGroup] = resource_group
  add_check(evidence, "azure.resourceGroup", "pass", resource_group["id"])
else
  add_check(evidence, "azure.resourceGroup", "fail", resource_group_error)
end

app_registration, app_registration_error = capture_json(
  ["az", "ad", "app", "list", "--display-name", options[:app_name], "--query", "[0].{appId:appId,id:id,displayName:displayName}", "--output", "json"]
)
if app_registration && app_registration["appId"]
  evidence[:azureApp] = app_registration
  add_check(evidence, "azure.appRegistration", "pass", app_registration["appId"])
else
  add_check(evidence, "azure.appRegistration", "fail", app_registration_error || "App registration #{options[:app_name]} was not found.")
end

app_id = app_registration && app_registration["appId"]
service_principal = nil
if app_id
  service_principal, service_principal_error = capture_json(
    ["az", "ad", "sp", "show", "--id", app_id, "--query", "{id:id,appId:appId,displayName:displayName}", "--output", "json"]
  )
  if service_principal
    evidence[:azureServicePrincipal] = service_principal
    add_check(evidence, "azure.servicePrincipal", "pass", service_principal["id"])
  else
    add_check(evidence, "azure.servicePrincipal", "fail", service_principal_error)
  end

  credentials, credentials_error = capture_json(
    ["az", "ad", "app", "federated-credential", "list", "--id", app_id, "--output", "json"]
  )
  if credentials
    matching = Array(credentials).find do |credential|
      credential["name"] == credential_name &&
        credential["issuer"] == AZURE_OIDC_ISSUER &&
        credential["subject"] == subject &&
        Array(credential["audiences"]).include?(AZURE_OIDC_AUDIENCE)
    end
    evidence[:federatedCredentialNames] = Array(credentials).map { |credential| credential["name"] }.compact.sort
    add_check(evidence, "azure.federatedCredential", matching ? "pass" : "fail", matching ? subject : "Expected #{credential_name} for #{subject}.")
  else
    add_check(evidence, "azure.federatedCredential", "fail", credentials_error)
  end
end

principal_id = service_principal && service_principal["id"]
if principal_id && subscription_id
  REQUIRED_RESOURCE_GROUP_ROLES.each do |role|
    assignments, assignments_error = capture_json(
      ["az", "role", "assignment", "list", "--assignee", principal_id, "--role", role, "--scope", scope, "--output", "json"]
    )
    if assignments
      add_check(evidence, "azure.role.#{role}", role_assignment_present?(assignments, role, scope) ? "pass" : "fail", scope)
    else
      add_check(evidence, "azure.role.#{role}", "fail", assignments_error)
    end
  end
else
  REQUIRED_RESOURCE_GROUP_ROLES.each do |role|
    add_check(evidence, "azure.role.#{role}", "fail", "Service principal and subscription id are required to verify role assignments.")
  end
end

failures = evidence[:checks].count { |check| check[:status] == "fail" }
warnings = evidence[:checks].count { |check| check[:status] == "warn" }
evidence[:ready] = failures.zero?
evidence[:summary] = {
  pass: evidence[:checks].count { |check| check[:status] == "pass" },
  warn: warnings,
  fail: failures
}

FileUtils.mkdir_p(File.dirname(options[:output]))
File.write(options[:output], "#{JSON.pretty_generate(evidence)}\n")

puts "Azure OIDC readiness audit written to #{options[:output]}"
puts "Environment: #{options[:environment]}"
puts "Ready: #{evidence[:ready]}"
puts "Checks: #{evidence[:summary][:pass]} pass, #{warnings} warn, #{failures} fail"

exit 1 if options[:strict] && !evidence[:ready]
