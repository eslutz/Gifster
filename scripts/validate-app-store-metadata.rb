#!/usr/bin/env ruby
# frozen_string_literal: true

require "uri"

ROOT = File.expand_path("..", __dir__)
METADATA_PATH = File.join(ROOT, "Documentation", "APP_STORE_METADATA.md")
REVIEW_NOTES_PATH = File.join(ROOT, "Documentation", "APP_REVIEW_NOTES.md")
PRIVACY_POLICY_PATH = File.join(ROOT, "Documentation", "PRIVACY_POLICY.md")

FIELD_LIMITS = {
  "Name" => 30,
  "Subtitle" => 30,
  "Promotional Text" => 170,
  "Description" => 4_000,
  "Keywords" => 100,
  "What's New" => 4_000
}.freeze

REQUIRED_METADATA_SNIPPETS = [
  "Tracking: No.",
  "Data used for tracking: No.",
  "Photos access: User-selected images only",
  "App Review phone number has been entered directly in App Store Connect",
  "Attach screenshots for the containing app and Messages extension flows"
].freeze

REQUIRED_REVIEW_NOTE_SNIPPETS = [
  "Gifster is an iMessage app extension",
  "Gifster does not auto-send messages",
  "Sticker mode is not implemented in v1",
  "The iOS app does not call external AI media providers directly",
  "Visible caption text is rendered locally",
  "The app does not request broad photo library access in v1",
  "Image Playground is not part of the v1 workflow"
].freeze

REQUIRED_PRIVACY_SNIPPETS = [
  "Gifster uses only images selected by the user",
  "Before upload, selected images are downscaled and rewritten as JPEG data",
  "External AI media providers are used only through the backend",
  "Captions are rendered locally into the final GIF",
  "Deployed defaults expire remaining job records after 24 hours",
  "Temporary provider result and source-image blobs are deleted by Azure Storage lifecycle policy after 2 days",
  "Gifster does not use data for tracking"
].freeze

def section_body(markdown, heading)
  pattern = /^## #{Regexp.escape(heading)}\n(?<body>.*?)(?=^## |\z)/m
  match = markdown.match(pattern)
  match ? match[:body].strip : nil
end

def bullet_value(section, label)
  section&.match(/^- #{Regexp.escape(label)}:\s*(?<value>.+)$/)&.[](:value)&.strip
end

def url_value(markdown, heading)
  section_body(markdown, heading)&.lines&.find { |line| line.match?(%r{\Ahttps?://}) }&.strip
end

def validate_url(value, label, errors)
  if value.nil? || value.empty?
    errors << "#{label} is missing."
    return
  end

  uri = URI.parse(value)
  unless uri.is_a?(URI::HTTP) && uri.host
    errors << "#{label} must be an absolute HTTP(S) URL, found #{value.inspect}."
  end
rescue URI::InvalidURIError
  errors << "#{label} must be a valid URL, found #{value.inspect}."
end

def validate_length(label, value, limit, errors)
  if value.nil? || value.empty?
    errors << "#{label} is missing."
    return
  end

  length = value.length
  return if length <= limit

  errors << "#{label} is #{length} characters; App Store limit is #{limit}."
end

metadata = File.read(METADATA_PATH)
review_notes = File.read(REVIEW_NOTES_PATH)
privacy_policy = File.read(PRIVACY_POLICY_PATH)
errors = []

app_information = section_body(metadata, "App Information")
field_values = {
  "Name" => bullet_value(app_information, "Name"),
  "Subtitle" => bullet_value(app_information, "Subtitle"),
  "Promotional Text" => section_body(metadata, "Promotional Text"),
  "Description" => section_body(metadata, "Description"),
  "Keywords" => section_body(metadata, "Keywords"),
  "What's New" => section_body(metadata, "What's New")
}

FIELD_LIMITS.each do |field, limit|
  validate_length(field, field_values[field], limit, errors)
end

keywords = field_values["Keywords"]
if keywords
  errors << "Keywords must be comma-separated without spaces." if keywords.match?(/\s/)
  errors << "Keywords must not contain duplicate entries." if keywords.split(",").map(&:downcase).uniq.length != keywords.split(",").length
end

validate_url(url_value(metadata, "Support URL"), "Support URL", errors)
validate_url(url_value(metadata, "Marketing URL"), "Marketing URL", errors)
validate_url(url_value(metadata, "Privacy Policy URL"), "Privacy Policy URL", errors)

metadata_contact = section_body(metadata, "App Review Contact")
phone_value = bullet_value(metadata_contact, "Phone")
if phone_value.nil? || !phone_value.include?("enter directly in App Store Connect")
  errors << "App Review Contact phone must remain an App Store Connect-only instruction, not a committed private phone number."
end

REQUIRED_METADATA_SNIPPETS.each do |snippet|
  errors << "#{METADATA_PATH.delete_prefix("#{ROOT}/")} must include #{snippet.inspect}." unless metadata.include?(snippet)
end

REQUIRED_REVIEW_NOTE_SNIPPETS.each do |snippet|
  errors << "#{REVIEW_NOTES_PATH.delete_prefix("#{ROOT}/")} must include #{snippet.inspect}." unless review_notes.include?(snippet)
end

REQUIRED_PRIVACY_SNIPPETS.each do |snippet|
  errors << "#{PRIVACY_POLICY_PATH.delete_prefix("#{ROOT}/")} must include #{snippet.inspect}." unless privacy_policy.include?(snippet)
end

if errors.any?
  warn "App Store metadata validation failed:"
  errors.each { |error| warn "- #{error}" }
  exit 1
end

puts "App Store metadata validation passed."
puts "Name: #{field_values.fetch("Name").length}/#{FIELD_LIMITS.fetch("Name")}"
puts "Subtitle: #{field_values.fetch("Subtitle").length}/#{FIELD_LIMITS.fetch("Subtitle")}"
puts "Promotional Text: #{field_values.fetch("Promotional Text").length}/#{FIELD_LIMITS.fetch("Promotional Text")}"
puts "Description: #{field_values.fetch("Description").length}/#{FIELD_LIMITS.fetch("Description")}"
puts "Keywords: #{field_values.fetch("Keywords").length}/#{FIELD_LIMITS.fetch("Keywords")}"
puts "What's New: #{field_values.fetch("What's New").length}/#{FIELD_LIMITS.fetch("What's New")}"
