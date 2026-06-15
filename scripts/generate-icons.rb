#!/usr/bin/env ruby
# frozen_string_literal: true

require "fileutils"
require "zlib"

ROOT = File.expand_path("..", __dir__)

APP_ICON = File.join(ROOT, "Client", "App", "Gifster", "Assets.xcassets", "AppIcon.appiconset")
MESSAGES_ICON = File.join(ROOT, "Client", "Extensions", "GifsterMessages", "Assets.xcassets", "iMessage App Icon.stickersiconset")

def lerp(a, b, t)
  (a + ((b - a) * t)).round
end

def blend(base, top)
  alpha = top[3] / 255.0
  [
    lerp(base[0], top[0], alpha),
    lerp(base[1], top[1], alpha),
    lerp(base[2], top[2], alpha),
    255
  ]
end

def inside_round_rect?(x, y, cx, cy, width, height, radius)
  qx = (x - cx).abs - (width / 2.0) + radius
  qy = (y - cy).abs - (height / 2.0) + radius
  [qx, 0].max**2 + [qy, 0].max**2 <= radius**2
end

def inside_triangle?(px, py, ax, ay, bx, by, cx, cy)
  d1 = ((px - bx) * (ay - by)) - ((ax - bx) * (py - by))
  d2 = ((px - cx) * (by - cy)) - ((bx - cx) * (py - cy))
  d3 = ((px - ax) * (cy - ay)) - ((cx - ax) * (py - ay))
  has_neg = d1.negative? || d2.negative? || d3.negative?
  has_pos = d1.positive? || d2.positive? || d3.positive?
  !(has_neg && has_pos)
end

def write_png(path, width, height)
  raw = String.new.b

  height.times do |py|
    raw << 0
    y = py + 0.5

    width.times do |px|
      x = px + 0.5
      tx = x / width
      ty = y / height

      base = [
        lerp(8, 18, tx * 0.35 + ty * 0.25),
        lerp(25, 96, ty),
        lerp(38, 112, tx * 0.4 + ty * 0.5),
        255
      ]

      cyan_glow = [35, 219, 188, 95]
      coral_glow = [255, 91, 112, 78]
      amber_glow = [255, 196, 74, 60]

      center_dx = x - (width * 0.24)
      center_dy = y - (height * 0.22)
      if (center_dx**2 + center_dy**2) < (width * 0.38)**2
        base = blend(base, cyan_glow)
      end

      corner_dx = x - (width * 0.88)
      corner_dy = y - (height * 0.82)
      if (corner_dx**2 + corner_dy**2) < (width * 0.42)**2
        base = blend(base, coral_glow)
      end

      amber_dx = x - (width * 0.72)
      amber_dy = y - (height * 0.20)
      if (amber_dx**2 + amber_dy**2) < (width * 0.28)**2
        base = blend(base, amber_glow)
      end

      min_dim = [width, height].min
      frame_w = width * 0.56
      frame_h = height * 0.36
      frame_r = min_dim * 0.055

      [
        [0.43, 0.43, [255, 205, 80, 225]],
        [0.50, 0.37, [46, 220, 190, 225]],
        [0.57, 0.43, [255, 92, 115, 225]]
      ].each do |cx_factor, cy_factor, color|
        if inside_round_rect?(x, y, width * cx_factor, height * cy_factor, frame_w, frame_h, frame_r)
          base = blend(base, color)
        end
      end

      shadow_w = width * 0.67
      shadow_h = height * 0.42
      shadow_r = min_dim * 0.085
      if inside_round_rect?(x, y, width * 0.53, height * 0.56, shadow_w, shadow_h, shadow_r)
        base = blend(base, [0, 0, 0, 70])
      end

      bubble_w = width * 0.66
      bubble_h = height * 0.41
      bubble_cx = width * 0.50
      bubble_cy = height * 0.52
      bubble_r = min_dim * 0.080
      in_bubble = inside_round_rect?(x, y, bubble_cx, bubble_cy, bubble_w, bubble_h, bubble_r)
      in_tail = inside_triangle?(
        x, y,
        width * 0.34, height * 0.68,
        width * 0.43, height * 0.66,
        width * 0.36, height * 0.79
      )

      if in_bubble || in_tail
        base = blend(base, [250, 254, 255, 245])
      end

      if in_bubble
        hole_w = min_dim * 0.035
        hole_h = min_dim * 0.035
        4.times do |index|
          hole_cx = width * (0.25 + index * 0.085)
          top_hole = height * 0.40
          bottom_hole = height * 0.64
          if inside_round_rect?(x, y, hole_cx, top_hole, hole_w, hole_h, hole_w * 0.25) ||
             inside_round_rect?(x, y, hole_cx, bottom_hole, hole_w, hole_h, hole_w * 0.25)
            base = blend(base, [20, 55, 76, 205])
          end
        end
      end

      play_ax = width * 0.48
      play_ay = height * 0.39
      play_bx = width * 0.48
      play_by = height * 0.65
      play_cx = width * 0.68
      play_cy = height * 0.52
      if inside_triangle?(x, y, play_ax, play_ay, play_bx, play_by, play_cx, play_cy)
        base = blend(base, [19, 167, 155, 235])
      end

      [
        [0.72, 0.35, 0.020, [255, 215, 96, 230]],
        [0.27, 0.30, 0.015, [255, 255, 255, 210]],
        [0.74, 0.68, 0.014, [255, 255, 255, 210]]
      ].each do |sx, sy, radius, color|
        dx = x - (width * sx)
        dy = y - (height * sy)
        if (dx.abs + dy.abs) < min_dim * radius || (dx**2 + dy**2) < (min_dim * radius * 0.42)**2
          base = blend(base, color)
        end
      end

      raw << base.pack("C4")
    end
  end

  compressed = Zlib::Deflate.deflate(raw)
  png = String.new.b
  png << "\x89PNG\r\n\x1a\n".b

  [
    ["IHDR", [width, height, 8, 6, 0, 0, 0].pack("NNC5")],
    ["IDAT", compressed],
    ["IEND", ""]
  ].each do |type, data|
    png << [data.bytesize].pack("N")
    png << type
    png << data
    png << [Zlib.crc32(type + data)].pack("N")
  end

  File.binwrite(path, png)
end

FileUtils.mkdir_p(APP_ICON)
FileUtils.mkdir_p(MESSAGES_ICON)

write_png(File.join(APP_ICON, "gifster-app-icon-1024.png"), 1024, 1024)

{
  "gifster-messages-58.png" => [58, 58],
  "gifster-messages-87.png" => [87, 87],
  "gifster-messages-120x90.png" => [120, 90],
  "gifster-messages-180x135.png" => [180, 135],
  "gifster-messages-134x100.png" => [134, 100],
  "gifster-messages-148x110.png" => [148, 110],
  "gifster-messages-54x40.png" => [54, 40],
  "gifster-messages-81x60.png" => [81, 60],
  "gifster-messages-64x48.png" => [64, 48],
  "gifster-messages-96x72.png" => [96, 72],
  "gifster-messages-1024x768.png" => [1024, 768]
}.each do |filename, size|
  write_png(File.join(MESSAGES_ICON, filename), size.fetch(0), size.fetch(1))
end

puts "Generated Gifster app and iMessage icon assets."
