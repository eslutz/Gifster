#!/usr/bin/env ruby
# frozen_string_literal: true

require "rexml/document"
require "yaml"

ROOT = File.expand_path("..", __dir__)

def parse_plist(path)
  document = REXML::Document.new(File.read(path))
  dict = document.root.elements["dict"]
  raise "No top-level dict in #{path}" unless dict

  elements = dict.elements.to_a
  values = {}
  index = 0

  while index < elements.length
    key = elements[index]
    value = elements[index + 1]
    index += 2

    next unless key&.name == "key" && value

    values[key.text] =
      case value.name
      when "string"
        value.text.to_s
      when "array"
        value.elements.map { |element| element.text.to_s }
      when "true"
        true
      when "false"
        false
      else
        value.text.to_s
      end
  end

  values
end

def target(project, name)
  project.fetch("targets").fetch(name)
end

def bundle_id(project, target_name)
  target(project, target_name).fetch("settings").fetch("base").fetch("PRODUCT_BUNDLE_IDENTIFIER")
end

def app_groups(project, target_name)
  target(project, target_name)
    .fetch("entitlements")
    .fetch("properties")
    .fetch("com.apple.security.application-groups")
end

project = YAML.load_file(File.join(ROOT, "Client", "project.yml"))

app_bundle_id = bundle_id(project, "Gifster")
extension_bundle_id = bundle_id(project, "GifsterMessagesExtension")
ui_tests_bundle_id = bundle_id(project, "GifsterUITests")

app_project_groups = app_groups(project, "Gifster")
extension_project_groups = app_groups(project, "GifsterMessagesExtension")

app_entitlements = parse_plist(File.join(ROOT, "Client", "App", "Gifster", "Gifster.entitlements"))
extension_entitlements = parse_plist(File.join(ROOT, "Client", "Extensions", "GifsterMessages", "GifsterMessages.entitlements"))

app_entitlement_groups = app_entitlements.fetch("com.apple.security.application-groups", [])
extension_entitlement_groups = extension_entitlements.fetch("com.apple.security.application-groups", [])
generated_project_path = File.join(ROOT, "Client", "Gifster.xcodeproj", "project.pbxproj")
generated_bundle_ids = File.read(generated_project_path)
  .scan(/PRODUCT_BUNDLE_IDENTIFIER = ([^;]+);/)
  .flatten
  .uniq

errors = []

unless extension_bundle_id.start_with?("#{app_bundle_id}.")
  errors << "Messages extension bundle id '#{extension_bundle_id}' must be prefixed by containing app bundle id '#{app_bundle_id}'."
end

unless ui_tests_bundle_id.start_with?("#{app_bundle_id}.")
  errors << "UI test bundle id '#{ui_tests_bundle_id}' should be prefixed by containing app bundle id '#{app_bundle_id}'."
end

[
  ["containing app", app_bundle_id],
  ["Messages extension", extension_bundle_id],
  ["UI tests", ui_tests_bundle_id]
].each do |name, expected_bundle_id|
  next if generated_bundle_ids.include?(expected_bundle_id)

  errors << "Generated Xcode project is missing expected #{name} bundle id '#{expected_bundle_id}'. Run xcodegen after updating Client/project.yml."
end

if app_project_groups.empty?
  errors << "Client/project.yml must declare at least one App Group for the containing app."
end

unless app_project_groups == extension_project_groups
  errors << "Client/project.yml App Groups differ between app #{app_project_groups.inspect} and extension #{extension_project_groups.inspect}."
end

unless app_entitlement_groups == app_project_groups
  errors << "Containing app entitlements App Groups #{app_entitlement_groups.inspect} do not match Client/project.yml #{app_project_groups.inspect}."
end

unless extension_entitlement_groups == extension_project_groups
  errors << "Messages extension entitlements App Groups #{extension_entitlement_groups.inspect} do not match Client/project.yml #{extension_project_groups.inspect}."
end

[
  ["containing app", app_entitlements],
  ["Messages extension", extension_entitlements]
].each do |name, entitlements|
  value = entitlements["com.apple.developer.devicecheck.appattest-environment"]
  next if value == "$(APP_ATTEST_ENVIRONMENT)"

  errors << "#{name} App Attest entitlement should use $(APP_ATTEST_ENVIRONMENT), found #{value.inspect}."
end

if errors.any?
  warn "Client signing validation failed:"
  errors.each { |error| warn "- #{error}" }
  exit 1
end

puts "Client signing validation passed."
puts "Containing app bundle id: #{app_bundle_id}"
puts "Messages extension bundle id: #{extension_bundle_id}"
puts "UI test bundle id: #{ui_tests_bundle_id}"
puts "App Groups: #{app_project_groups.join(", ")}"
