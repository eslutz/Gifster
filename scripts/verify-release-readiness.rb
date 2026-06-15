#!/usr/bin/env ruby
# frozen_string_literal: true

require "json"
require "yaml"

ROOT = File.expand_path("..", __dir__)

PROJECT_PATH = File.join(ROOT, "Client", "project.yml")
PACKAGE_PATH = File.join(ROOT, "Client", "Packages", "GifsterCore", "Package.swift")
APP_ICON_CONTENTS = File.join(ROOT, "Client", "App", "Gifster", "Assets.xcassets", "AppIcon.appiconset", "Contents.json")
MESSAGES_ICON_CONTENTS = File.join(ROOT, "Client", "Extensions", "GifsterMessages", "Assets.xcassets", "iMessage App Icon.stickersiconset", "Contents.json")

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
