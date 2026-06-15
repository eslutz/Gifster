#!/usr/bin/env ruby
# frozen_string_literal: true

require "json"
require "net/http"
require "optparse"
require "securerandom"
require "uri"

FRAME_SEQUENCE_CONTENT_TYPE = "application/vnd.gifster.frame-sequence+json"
MP4_CONTENT_TYPE = "video/mp4"

options = {
  mode: "text_to_gif",
  print_payload: false,
  timeout_seconds: Integer(ENV.fetch("GIFSTER_PROVIDER_PRECHECK_TIMEOUT_SECONDS", "120"))
}

OptionParser.new do |parser|
  parser.banner = "Usage: scripts/validate-external-provider-contract.rb [options]"

  parser.on("--mode MODE", "text_to_gif or image_to_gif. Default: text_to_gif.") do |mode|
    options[:mode] = mode
  end

  parser.on("--print-payload", "Print the sanitized provider payload and exit without network calls.") do
    options[:print_payload] = true
  end

  parser.on("--timeout SECONDS", Integer, "HTTP timeout in seconds. Default: GIFSTER_PROVIDER_PRECHECK_TIMEOUT_SECONDS or 120.") do |seconds|
    options[:timeout_seconds] = seconds
  end

  parser.on("-h", "--help", "Show this help.") do
    puts parser
    exit
  end
end.parse!

unless %w[text_to_gif image_to_gif].include?(options[:mode])
  warn "Unsupported mode #{options[:mode].inspect}. Expected text_to_gif or image_to_gif."
  exit 2
end

def require_env(name)
  value = ENV[name]
  return value unless value.nil? || value.strip.empty?

  warn "Missing required environment variable: #{name}"
  exit 2
end

def optional_env(name)
  value = ENV[name]
  value.nil? || value.strip.empty? ? nil : value
end

def source_image_payload
  data_base64 = require_env("GIFSTER_PROVIDER_PRECHECK_IMAGE_BASE64")
  width = Integer(require_env("GIFSTER_PROVIDER_PRECHECK_IMAGE_WIDTH"))
  height = Integer(require_env("GIFSTER_PROVIDER_PRECHECK_IMAGE_HEIGHT"))
  mime_type = ENV.fetch("GIFSTER_PROVIDER_PRECHECK_IMAGE_MIME_TYPE", "image/jpeg")

  {
    dataBase64: data_base64,
    mimeType: mime_type,
    width: width,
    height: height
  }
end

def source_image_context(source_image)
  width = source_image.fetch(:width)
  height = source_image.fetch(:height)
  orientation = width >= height ? "landscape" : "portrait"
  aspect_ratio = "#{width}:#{height}"

  {
    width: width,
    height: height,
    orientation: orientation,
    aspectRatio: aspect_ratio,
    summary: "Provider preflight source image, #{width}x#{height}, aspect #{aspect_ratio}."
  }
end

def provider_payload(mode)
  source_image = mode == "image_to_gif" ? source_image_payload : nil

  {
    id: "provider-preflight-#{SecureRandom.uuid}",
    mode: mode,
    cleanedPrompt: "a tiny robot waving from a desk",
    expandedPrompt: "Create a short seamless animated loop of a tiny robot waving from a desk. Do not render readable text.",
    negativePrompt: "readable text, captions, subtitles, logos, watermarks",
    captionMode: "none",
    renderCaptionLocally: true,
    sourceImage: source_image,
    sourceImageContext: source_image ? source_image_context(source_image) : nil,
    options: {
      width: 480,
      height: 360,
      loopSeconds: 2,
      stylePreset: "expressive-loop",
      motionIntensity: "medium"
    },
    clientTraceId: "external-provider-preflight"
  }
end

def request(uri, method, timeout_seconds, authorization_header: nil, body: nil)
  http = Net::HTTP.new(uri.host, uri.port)
  http.use_ssl = uri.scheme == "https"
  http.open_timeout = timeout_seconds
  http.read_timeout = timeout_seconds
  http.write_timeout = timeout_seconds if http.respond_to?(:write_timeout=)

  request = method == :post ? Net::HTTP::Post.new(uri) : Net::HTTP::Get.new(uri)
  request["Authorization"] = authorization_header if authorization_header

  if body
    request["Content-Type"] = "application/json"
    request.body = JSON.generate(body)
  end

  http.request(request)
end

def provider_job_id(response)
  payload = JSON.parse(response.body)
  value = payload["providerJobId"]
  return value if value.is_a?(String) && !value.strip.empty?

  warn "Provider submission response did not include providerJobId."
  exit 1
rescue JSON::ParserError => e
  warn "Provider submission response was not valid JSON: #{e.message}"
  exit 1
end

def result_url(template, provider_job_id, request_id)
  URI(
    template
      .gsub("{providerJobId}", URI.encode_www_form_component(provider_job_id))
      .gsub("{jobId}", URI.encode_www_form_component(request_id))
  )
end

def validate_result(response)
  content_type = response["content-type"].to_s.split(";").first
  if content_type == MP4_CONTENT_TYPE
    return if response.body && !response.body.empty?

    warn "Provider result was video/mp4 but had an empty body."
    exit 1
  end

  if content_type == FRAME_SEQUENCE_CONTENT_TYPE
    payload = JSON.parse(response.body)
    unless payload["format"] == "frame-sequence-v1" && payload["frames"].is_a?(Array) && payload["frames"].any?
      warn "Provider frame-sequence result must include format=frame-sequence-v1 and at least one frame."
      exit 1
    end

    return
  end

  warn "Unsupported provider result content type #{content_type.inspect}. Expected #{FRAME_SEQUENCE_CONTENT_TYPE} or #{MP4_CONTENT_TYPE}."
  exit 1
rescue JSON::ParserError => e
  warn "Provider frame-sequence result was not valid JSON: #{e.message}"
  exit 1
end

def retryable_result_status?(status)
  status == 202 ||
    status == 204 ||
    status == 404 ||
    status == 409 ||
    status == 425 ||
    status == 429 ||
    status >= 500
end

def permanent_result_status?(status)
  [400, 401, 403, 422].include?(status)
end

payload = provider_payload(options[:mode])

if options[:print_payload]
  puts JSON.pretty_generate(payload)
  exit
end

submit_url = URI(require_env("GIFSTER_EXTERNAL_PROVIDER_SUBMIT_URL"))
result_template = require_env("GIFSTER_EXTERNAL_PROVIDER_RESULT_URL_TEMPLATE")
authorization = optional_env("GIFSTER_EXTERNAL_PROVIDER_AUTHORIZATION")

puts "Validating external provider contract at #{submit_url}"
submit_response = request(
  submit_url,
  :post,
  options[:timeout_seconds],
  authorization_header: authorization,
  body: payload
)

unless submit_response.code.to_i.between?(200, 299)
  warn "Provider submission failed with HTTP #{submit_response.code}."
  exit 1
end

job_id = provider_job_id(submit_response)
download_url = result_url(result_template, job_id, payload.fetch(:id))
puts "Provider accepted preflight job #{job_id}; downloading #{download_url}"

deadline = Time.now + options[:timeout_seconds]
loop do
  result_response = request(
    download_url,
    :get,
    options[:timeout_seconds],
    authorization_header: authorization
  )
  status = result_response.code.to_i

  if status.between?(200, 299) && ![202, 204].include?(status)
    validate_result(result_response)
    break
  end

  if permanent_result_status?(status)
    warn "Provider result failed permanently with HTTP #{status}."
    exit 1
  end

  unless retryable_result_status?(status)
    warn "Provider result failed with unexpected HTTP #{status}."
    exit 1
  end

  if Time.now >= deadline
    warn "Provider result was not ready within #{options[:timeout_seconds]} seconds; last HTTP status was #{status}."
    exit 1
  end

  sleep [2, [deadline - Time.now, 0].max].min
end

puts "External provider contract preflight passed for #{job_id}."
