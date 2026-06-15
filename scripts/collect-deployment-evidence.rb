#!/usr/bin/env ruby
# frozen_string_literal: true

require "fileutils"
require "json"
require "net/http"
require "open3"
require "optparse"
require "time"
require "uri"

ROOT = File.expand_path("..", __dir__)
DEFAULT_REPO = "eslutz/Gifster"

options = {
  environment: "nonprod",
  repo: DEFAULT_REPO,
  output: nil,
  resource_group: nil,
  backend_url: nil,
  workflow_run_id: nil,
  timeout_seconds: 10
}

OptionParser.new do |parser|
  parser.banner = "Usage: scripts/collect-deployment-evidence.rb [options]"

  parser.on("--environment NAME", "Deployment environment: nonprod or prod. Default: nonprod.") do |value|
    options[:environment] = value
  end

  parser.on("--repo OWNER/REPO", "GitHub repository. Default: #{DEFAULT_REPO}.") do |value|
    options[:repo] = value
  end

  parser.on("--resource-group NAME", "Azure resource group. Default: rg-gifster-<environment>.") do |value|
    options[:resource_group] = value
  end

  parser.on("--backend-url URL", "Backend URL to health-check. Defaults to the discovered API Container App FQDN.") do |value|
    options[:backend_url] = value
  end

  parser.on("--workflow-run-id ID", "GitHub Actions deployment run id to include.") do |value|
    options[:workflow_run_id] = value
  end

  parser.on("--output PATH", "JSON evidence output path. Default: Documentation/DeploymentEvidence/<env>-<timestamp>.json.") do |value|
    options[:output] = value
  end

  parser.on("--timeout SECONDS", Integer, "HTTP health-check timeout in seconds. Default: 10.") do |value|
    options[:timeout_seconds] = value
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

options[:resource_group] ||= "rg-gifster-#{options[:environment]}"
timestamp = Time.now.utc.strftime("%Y%m%dT%H%M%SZ")
options[:output] ||= File.join(
  ROOT,
  "Documentation",
  "DeploymentEvidence",
  "#{options[:environment]}-#{timestamp}.json"
)

def run_json(command, required: true)
  output, status = Open3.capture2e(*command)
  if status.success?
    return nil if output.strip.empty?

    JSON.parse(output)
  elsif required
    raise "#{command.join(" ")} failed: #{output.strip}"
  end
rescue JSON::ParserError => e
  raise "#{command.join(" ")} returned invalid JSON: #{e.message}"
end

def run_text(command, required: true)
  output, status = Open3.capture2e(*command)
  return output.strip if status.success?

  raise "#{command.join(" ")} failed: #{output.strip}" if required

  nil
end

def safe_container_app(app)
  template = app.dig("properties", "template") || {}
  configuration = app.dig("properties", "configuration") || {}
  scale = template["scale"] || {}
  container = Array(template["containers"]).first || {}

  {
    name: app["name"],
    location: app["location"],
    image: container["image"],
    provisioningState: app.dig("properties", "provisioningState"),
    runningStatus: app.dig("properties", "runningStatus"),
    latestRevisionName: app.dig("properties", "latestRevisionName"),
    latestReadyRevisionName: app.dig("properties", "latestReadyRevisionName"),
    activeRevisionsMode: configuration["activeRevisionsMode"],
    fqdn: configuration.dig("ingress", "fqdn"),
    targetPort: configuration.dig("ingress", "targetPort"),
    minReplicas: scale["minReplicas"],
    maxReplicas: scale["maxReplicas"],
    scaleRules: Array(scale["rules"]).map do |rule|
      {
        name: rule["name"],
        customType: rule.dig("custom", "type"),
        metadata: rule.dig("custom", "metadata")
      }
    end,
    envNames: Array(container["env"]).map { |entry| entry["name"] }.compact.sort
  }
end

def health_check(backend_url, timeout_seconds)
  return nil if backend_url.nil? || backend_url.strip.empty?

  uri = URI.join(backend_url.end_with?("/") ? backend_url : "#{backend_url}/", "health")
  response = Net::HTTP.start(uri.host, uri.port, use_ssl: uri.scheme == "https",
                             open_timeout: timeout_seconds, read_timeout: timeout_seconds) do |http|
    http.get(uri.request_uri)
  end

  {
    url: uri.to_s,
    status: response.code.to_i,
    body: JSON.parse(response.body)
  }
rescue JSON::ParserError
  {
    url: uri&.to_s,
    status: response&.code&.to_i,
    body: response&.body
  }
rescue StandardError => e
  {
    url: backend_url,
    error: e.message
  }
end

evidence = {
  collectedAt: Time.now.utc.iso8601,
  environment: options[:environment],
  repository: options[:repo],
  resourceGroup: options[:resource_group],
  localGit: {
    head: run_text(%w[git rev-parse HEAD], required: false),
    branch: run_text(%w[git branch --show-current], required: false),
    status: run_text(%w[git status --short], required: false)
  }
}

begin
  evidence[:azureAccount] = run_json(
    %w[az account show --query] + ["{subscriptionId:id,tenantId:tenantId,name:name,user:user.name}"] + %w[--output json],
    required: false
  )
  evidence[:azureResourceGroup] = run_json(
    ["az", "group", "show", "--name", options[:resource_group], "--query", "{id:id,name:name,location:location,tags:tags}", "--output", "json"],
    required: false
  )
  apps = run_json(
    ["az", "containerapp", "list", "--resource-group", options[:resource_group], "--output", "json"],
    required: false
  ) || []
  evidence[:containerApps] = apps
    .select { |app| app["name"].to_s.start_with?("gifster-#{options[:environment]}-") }
    .map { |app| safe_container_app(app) }
    .sort_by { |app| app[:name].to_s }
rescue StandardError => e
  evidence[:azureError] = e.message
end

if options[:workflow_run_id]
  evidence[:githubDeploymentRun] = run_json(
    ["gh", "run", "view", options[:workflow_run_id], "--repo", options[:repo], "--json", "databaseId,name,displayTitle,event,headSha,status,conclusion,createdAt,updatedAt,url,jobs"],
    required: false
  )
end

api_app = Array(evidence[:containerApps]).find { |app| app[:name].to_s.end_with?("-api") }
backend_url = options[:backend_url]
backend_url ||= "https://#{api_app[:fqdn]}" if api_app && api_app[:fqdn]
evidence[:health] = health_check(backend_url, options[:timeout_seconds]) if backend_url

FileUtils.mkdir_p(File.dirname(options[:output]))
File.write(options[:output], "#{JSON.pretty_generate(evidence)}\n")

puts "Deployment evidence written to #{options[:output]}"
puts "Environment: #{options[:environment]}"
puts "Resource group: #{options[:resource_group]}"
if evidence[:health]
  puts "Health: #{evidence[:health][:status] || evidence[:health][:error]}"
end
