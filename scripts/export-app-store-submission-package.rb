#!/usr/bin/env ruby
# frozen_string_literal: true

require "fileutils"
require "json"
require "optparse"
require "time"

ROOT = File.expand_path("..", __dir__)
METADATA_PATH = File.join(ROOT, "Documentation", "APP_STORE_METADATA.md")
REVIEW_NOTES_PATH = File.join(ROOT, "Documentation", "APP_REVIEW_NOTES.md")
PRIVACY_POLICY_PATH = File.join(ROOT, "Documentation", "PRIVACY_POLICY.md")
DEFAULT_CONTAINING_SCREENSHOT_DIR = File.join(ROOT, "Documentation", "AppStoreScreenshots", "containing-app")
DEFAULT_MESSAGES_SCREENSHOT_DIR = File.join(ROOT, "Documentation", "AppStoreScreenshots", "messages-extension")
EXPECTED_CONTAINING_SCREENSHOTS = %w[
  01-containing-app-overview.png
  02-containing-app-history.png
  03-containing-app-clear-history.png
  04-containing-app-settings.png
].freeze

options = {
  output: nil,
  containing_screenshots: DEFAULT_CONTAINING_SCREENSHOT_DIR,
  messages_screenshots: DEFAULT_MESSAGES_SCREENSHOT_DIR,
  require_screenshots: false
}

OptionParser.new do |parser|
  parser.banner = "Usage: scripts/export-app-store-submission-package.rb [options]"

  parser.on("--output DIR", "Output package directory. Default: Documentation/AppStoreSubmission/<timestamp>.") do |value|
    options[:output] = value
  end

  parser.on("--containing-screenshots DIR", "Containing-app screenshot directory.") do |value|
    options[:containing_screenshots] = value
  end

  parser.on("--messages-screenshots DIR", "Messages extension screenshot directory.") do |value|
    options[:messages_screenshots] = value
  end

  parser.on("--require-screenshots", "Exit 1 if containing-app or Messages screenshots are missing.") do
    options[:require_screenshots] = true
  end

  parser.on("-h", "--help", "Show this help.") do
    puts parser
    exit
  end
end.parse!

timestamp = Time.now.utc.strftime("%Y%m%dT%H%M%SZ")
options[:output] ||= File.join(ROOT, "Documentation", "AppStoreSubmission", timestamp)

def relative(path)
  path.delete_prefix("#{ROOT}/")
end

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

def png_dimensions(path)
  bytes = File.binread(path, 24)
  signature = "\x89PNG\r\n\x1a\n".b
  raise "#{path} is not a PNG file." unless bytes.start_with?(signature)

  width, height = bytes.byteslice(16, 8).unpack("NN")
  [width, height]
end

def screenshot_entry(path)
  width, height = png_dimensions(path)
  {
    fileName: File.basename(path),
    sourcePath: path,
    bytes: File.size(path),
    pixelWidth: width,
    pixelHeight: height
  }
end

def containing_screenshot_manifest(directory)
  entries = []
  missing = []

  EXPECTED_CONTAINING_SCREENSHOTS.each do |name|
    path = File.join(directory, name)
    if File.file?(path)
      entries << screenshot_entry(path)
    else
      missing << name
    end
  end

  {
    directory: directory,
    expected: EXPECTED_CONTAINING_SCREENSHOTS,
    screenshots: entries,
    missing: missing
  }
end

def messages_screenshot_manifest(directory)
  paths = Dir.glob(File.join(directory, "*.png")).sort
  {
    directory: directory,
    screenshots: paths.map { |path| screenshot_entry(path) },
    missing: paths.empty? ? ["Messages extension compact/expanded screenshots are required from physical Messages testing."] : []
  }
end

metadata = File.read(METADATA_PATH)
review_notes = File.read(REVIEW_NOTES_PATH)
privacy_policy = File.read(PRIVACY_POLICY_PATH)
app_information = section_body(metadata, "App Information")

app_store_fields = {
  name: bullet_value(app_information, "Name"),
  subtitle: bullet_value(app_information, "Subtitle"),
  category: bullet_value(app_information, "Category"),
  secondaryCategory: bullet_value(app_information, "Secondary category"),
  contentRights: bullet_value(app_information, "Content rights"),
  promotionalText: section_body(metadata, "Promotional Text"),
  description: section_body(metadata, "Description"),
  keywords: section_body(metadata, "Keywords"),
  whatsNew: section_body(metadata, "What's New"),
  supportUrl: url_value(metadata, "Support URL"),
  marketingUrl: url_value(metadata, "Marketing URL"),
  privacyPolicyUrl: url_value(metadata, "Privacy Policy URL"),
  demoAccount: section_body(metadata, "Demo Account"),
  appPrivacyAnswers: section_body(metadata, "App Privacy Answers"),
  reviewChecklist: section_body(metadata, "Review Checklist Before Submission")
}

containing_manifest = containing_screenshot_manifest(options[:containing_screenshots])
messages_manifest = messages_screenshot_manifest(options[:messages_screenshots])
blockers = []
blockers.concat(containing_manifest[:missing].map { |name| "Missing containing-app screenshot #{name}." })
blockers.concat(messages_manifest[:missing])
blockers << "App Review phone number must be entered directly in App Store Connect." if metadata.include?("enter directly in App Store Connect")
blockers << "Replace GitHub fallback URLs with product-site URLs if a dedicated public site is available." if metadata.include?("github.com/eslutz/Gifster")

package = {
  exportedAt: Time.now.utc.iso8601,
  sourceCommit: `git rev-parse HEAD 2>/dev/null`.strip,
  metadataSource: relative(METADATA_PATH),
  reviewNotesSource: relative(REVIEW_NOTES_PATH),
  privacyPolicySource: relative(PRIVACY_POLICY_PATH),
  appStoreFields: app_store_fields,
  screenshots: {
    containingApp: containing_manifest,
    messagesExtension: messages_manifest
  },
  readyForManualAppStoreConnectEntry: blockers.empty?,
  blockers: blockers
}

FileUtils.rm_rf(options[:output])
FileUtils.mkdir_p(options[:output])
FileUtils.mkdir_p(File.join(options[:output], "screenshots", "containing-app"))
FileUtils.mkdir_p(File.join(options[:output], "screenshots", "messages-extension"))

File.write(File.join(options[:output], "metadata.json"), "#{JSON.pretty_generate(package)}\n")
File.write(File.join(options[:output], "app-review-notes.md"), review_notes)
File.write(File.join(options[:output], "privacy-policy.md"), privacy_policy)
File.write(
  File.join(options[:output], "README.md"),
  <<~MARKDOWN
    # Gifster App Store Submission Package

    Generated at #{package[:exportedAt]} from commit `#{package[:sourceCommit]}`.

    - `metadata.json` contains structured App Store Connect fields and screenshot manifests.
    - `app-review-notes.md` is the App Review note draft.
    - `privacy-policy.md` is the public privacy policy draft.
    - `screenshots/` contains copied screenshot PNGs when available.

    Ready for manual App Store Connect entry: #{package[:readyForManualAppStoreConnectEntry]}.
  MARKDOWN
)

containing_manifest[:screenshots].each do |entry|
  FileUtils.cp(entry[:sourcePath], File.join(options[:output], "screenshots", "containing-app", entry[:fileName]))
end

messages_manifest[:screenshots].each do |entry|
  FileUtils.cp(entry[:sourcePath], File.join(options[:output], "screenshots", "messages-extension", entry[:fileName]))
end

puts "App Store submission package written to #{options[:output]}"
puts "Containing-app screenshots: #{containing_manifest[:screenshots].length}/#{EXPECTED_CONTAINING_SCREENSHOTS.length}"
puts "Messages extension screenshots: #{messages_manifest[:screenshots].length}"
puts "Ready for manual App Store Connect entry: #{package[:readyForManualAppStoreConnectEntry]}"

if options[:require_screenshots] && (containing_manifest[:missing].any? || messages_manifest[:missing].any?)
  warn "Required screenshots are missing:"
  (containing_manifest[:missing] + messages_manifest[:missing]).each { |missing| warn "- #{missing}" }
  exit 1
end
