#!/usr/bin/env ruby
# frozen_string_literal: true

require "fileutils"
require "json"
require "open3"
require "optparse"
require "time"

ROOT = File.expand_path("..", __dir__)
DEFAULT_RESOURCE_GROUP = "ericslutz.dev-resource-group"
DEFAULT_SERVER = "ericslutz-dev-db"
DEFAULT_DATABASE = "ericslutz.dev.db"
REQUIRED_TABLES = %w[
  users
  refresh_tokens
  iap_products
  iap_transactions
  credit_reservations
  usage_ledger
  generation_ownership
  auth_events
  purchase_events
].freeze
REQUIRED_PRODUCTS = %w[
  dev.ericslutz.gifforge.credits.10
  dev.ericslutz.gifforge.credits.25
  dev.ericslutz.gifforge.credits.55
].freeze

options = {
  environment: "nonprod",
  resource_group: DEFAULT_RESOURCE_GROUP,
  server: DEFAULT_SERVER,
  database: DEFAULT_DATABASE,
  output: nil,
  strict: false
}

OptionParser.new do |parser|
  parser.banner = "Usage: scripts/validate-sql-readiness.rb [options]"

  parser.on("--environment NAME", "Environment label for evidence output. Default: nonprod.") do |value|
    options[:environment] = value
  end

  parser.on("--resource-group NAME", "Azure SQL resource group. Default: #{DEFAULT_RESOURCE_GROUP}.") do |value|
    options[:resource_group] = value
  end

  parser.on("--server NAME", "Azure SQL server name. Default: #{DEFAULT_SERVER}.") do |value|
    options[:server] = value
  end

  parser.on("--database NAME", "Azure SQL database name. Default: #{DEFAULT_DATABASE}.") do |value|
    options[:database] = value
  end

  parser.on("--output PATH", "JSON output path. Default: Documentation/DeploymentEvidence/<env>-sql-<timestamp>.json.") do |value|
    options[:output] = value
  end

  parser.on("--strict", "Exit 1 when any required readiness check fails.") do
    options[:strict] = true
  end

  parser.on("-h", "--help", "Show this help.") do
    puts parser
    exit
  end
end.parse!

timestamp = Time.now.utc.strftime("%Y%m%dT%H%M%SZ")
options[:output] ||= File.join(
  ROOT,
  "Documentation",
  "DeploymentEvidence",
  "#{options[:environment]}-sql-#{timestamp}.json"
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

def add_check(evidence, name, status, detail = nil)
  entry = { name: name, status: status }
  entry[:detail] = detail if detail && !detail.to_s.empty?
  evidence[:checks] << entry
end

evidence = {
  collectedAt: Time.now.utc.iso8601,
  environment: options[:environment],
  resourceGroup: options[:resource_group],
  sqlServer: options[:server],
  sqlDatabase: options[:database],
  expectedFullyQualifiedDomainName: "#{options[:server]}.database.windows.net",
  requiredSchema: "gifforge",
  requiredTables: REQUIRED_TABLES,
  requiredProductIds: REQUIRED_PRODUCTS,
  checks: []
}

add_check(evidence, "tool.az", command_available?("az") ? "pass" : "fail", "az must be installed and authenticated.")

migration_path = File.join(ROOT, "Backend", "Database", "Migrations", "001_gifforge_accounts_iap_credits.sql")
if File.file?(migration_path)
  migration = File.read(migration_path)
  missing_tables = REQUIRED_TABLES.reject { |table| migration.include?("gifforge.#{table}") }
  missing_products = REQUIRED_PRODUCTS.reject { |product| migration.include?(product) }
  add_check(
    evidence,
    "migration.required_tables",
    missing_tables.empty? ? "pass" : "fail",
    missing_tables.empty? ? "All required table names are present." : "Missing: #{missing_tables.join(", ")}"
  )
  add_check(
    evidence,
    "migration.required_products",
    missing_products.empty? ? "pass" : "fail",
    missing_products.empty? ? "All required product ids are present." : "Missing: #{missing_products.join(", ")}"
  )
else
  add_check(evidence, "migration.file", "fail", "Missing #{migration_path}.")
end

recovery_migration_path = File.join(ROOT, "Backend", "Database", "Migrations", "002_optional_apple_recovery_accounts.sql")
if File.file?(recovery_migration_path)
  recovery_migration = File.read(recovery_migration_path)
  has_nullable_alter = recovery_migration.include?("ALTER COLUMN apple_subject nvarchar(255) NULL")
  has_filtered_index = recovery_migration.include?("WHERE apple_subject IS NOT NULL")
  add_check(
    evidence,
    "migration.optional_apple_recovery",
    has_nullable_alter && has_filtered_index ? "pass" : "fail",
    has_nullable_alter && has_filtered_index ? "Optional Apple recovery migration is present." : "Migration must make apple_subject nullable and keep a filtered unique index."
  )
else
  add_check(evidence, "migration.optional_apple_recovery", "fail", "Missing #{recovery_migration_path}.")
end

if command_available?("az")
  server, server_error = capture_json(
    [
      "az", "sql", "server", "show",
      "--resource-group", options[:resource_group],
      "--name", options[:server],
      "--query", "{name:name,fullyQualifiedDomainName:fullyQualifiedDomainName,resourceGroup:resourceGroup,location:location,publicNetworkAccess:publicNetworkAccess,minimalTlsVersion:minimalTlsVersion}",
      "--output", "json"
    ]
  )
  if server
    evidence[:server] = server
    add_check(evidence, "azure.sql_server", "pass", server["fullyQualifiedDomainName"])
    add_check(
      evidence,
      "azure.sql_server_tls",
      server["minimalTlsVersion"].to_s >= "1.2" ? "pass" : "fail",
      "minimalTlsVersion=#{server["minimalTlsVersion"]}"
    )
  else
    add_check(evidence, "azure.sql_server", "fail", server_error)
  end

  database, database_error = capture_json(
    [
      "az", "sql", "db", "show",
      "--resource-group", options[:resource_group],
      "--server", options[:server],
      "--name", options[:database],
      "--query", "{name:name,status:status,edition:edition,serviceLevelObjective:currentServiceObjectiveName,collation:collation,maxSizeBytes:maxSizeBytes}",
      "--output", "json"
    ]
  )
  if database
    evidence[:database] = database
    add_check(evidence, "azure.sql_database", "pass", "#{database["name"]} status=#{database["status"]}")
  else
    add_check(evidence, "azure.sql_database", "fail", database_error)
  end
end

if command_available?("sqlcmd")
  add_check(evidence, "tool.sqlcmd", "pass", "sqlcmd is available for live schema checks.")
  add_check(
    evidence,
    "live_schema",
    "skip",
    "Live schema queries are intentionally not run by default; use the migration script through an approved migration principal."
  )
else
  add_check(evidence, "tool.sqlcmd", "skip", "sqlcmd is not installed; Azure resource and migration-file checks were run.")
end

FileUtils.mkdir_p(File.dirname(options[:output]))
File.write(options[:output], "#{JSON.pretty_generate(evidence)}\n")

failures = evidence[:checks].select { |check| check[:status] == "fail" }
if failures.empty?
  puts "SQL readiness validation passed. Evidence written to #{options[:output]}."
  exit 0
end

warn "SQL readiness validation found #{failures.length} failure(s). Evidence written to #{options[:output]}."
exit(options[:strict] ? 1 : 0)
