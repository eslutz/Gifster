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
