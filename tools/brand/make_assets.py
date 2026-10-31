"""
WiPass brand assets generator (logo A: "forward signal").

Regenerates every bitmap brand asset from one geometry definition:
  src/WiPass/wipass.ico                   app icon (16-256 px, small sizes simplified)
  installer/wizard-large-*.bmp            Inno Setup wizard side image (100/150/200 %)
  installer/wizard-small-*.bmp            Inno Setup wizard header image (100/150/200 %)
  docs/images/banner.png                  README banner
  docs/images/social-preview.png          GitHub social preview (upload in repo Settings)

Usage:  python3 tools/brand/make_assets.py   (needs Pillow and a Korean font, e.g. Noto Sans CJK)
"""
import math
import os
import sys

from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))

TEAL = (15, 118, 110)        # #0F766E  brand
TEAL_DARK = (19, 78, 74)     # #134E4A  headings on light ground
TEAL_LIGHT = (153, 246, 228) # #99F6E4  accent on teal ground
MINT_BG = (240, 253, 250)    # #F0FDFA  light ground
WHITE = (255, 255, 255)
INK = (55, 65, 81)           # #374151  body text

FONT_CANDIDATES = [
    ("/usr/share/fonts/opentype/noto/NotoSansCJK-Bold.ttc", 1),
    ("C:/Windows/Fonts/malgunbd.ttf", 0),
]
FONT_REGULAR_CANDIDATES = [
    ("/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc", 1),
    ("C:/Windows/Fonts/malgun.ttf", 0),
]


def font(size, bold=True):
    for path, index in (FONT_CANDIDATES if bold else FONT_REGULAR_CANDIDATES):
        if os.path.exists(path):
            return ImageFont.truetype(path, size, index=index)
    sys.exit("Korean font not found (install Noto Sans CJK)")


# ---------------------------------------------------------------- the mark
# Geometry in a 64x64 box: three arcs opening to the right around (17, 32) plus a dot.
CENTER = (17.0, 32.0)
ARCS = (14.0, 24.0, 34.0)
DOT_R = 4.5
STROKE = 6.0


def draw_mark(img, box, color, simplified=False, stroke=None, ss=8):
    """Draws the mark into `img` inside `box` = (x, y, size). Supersampled for smooth edges."""
    x0, y0, size = box
    big = Image.new("RGBA", (size * ss, size * ss), (0, 0, 0, 0))
    d = ImageDraw.Draw(big)
    k = size * ss / 64.0
    arcs = ARCS[1:] if simplified else ARCS
    w = (stroke or (8.0 if simplified else STROKE)) * k
    dot = (6.0 if simplified else DOT_R) * k
    cx, cy = CENTER[0] * k, CENTER[1] * k
    for r in arcs:
        rr = r * k
        # PIL draws the arc band inward from the box edge: grow the box by half the stroke
        # so the band is centred on the radius (and meets the round caps).
        h = w / 2
        d.arc([cx - rr - h, cy - rr - h, cx + rr + h, cy + rr + h], -45, 45, fill=color, width=int(round(w)))
        for ang in (-45, 45):  # round caps
            ex = cx + rr * math.cos(math.radians(ang))
            ey = cy + rr * math.sin(math.radians(ang))
            d.ellipse([ex - w / 2, ey - w / 2, ex + w / 2, ey + w / 2], fill=color)
    d.ellipse([cx - dot, cy - dot, cx + dot, cy + dot], fill=color)
    small = big.resize((size, size), Image.LANCZOS)
    img.alpha_composite(small, (int(x0), int(y0)))


def app_icon(size):
    """Teal rounded tile with the white mark (small sizes use the simplified mark)."""
    ss = 8
    tile = Image.new("RGBA", (size * ss, size * ss), (0, 0, 0, 0))
    ImageDraw.Draw(tile).rounded_rectangle([0, 0, size * ss - 1, size * ss - 1],
                                           radius=int(size * ss * 0.22), fill=TEAL)
    img = tile.resize((size, size), Image.LANCZOS)
    simplified = size <= 16
    inner = int(round(size * (0.80 if simplified else 0.70)))
    off = (size - inner) // 2
    stroke = None if size > 40 else (7.0 if not simplified else 8.0)
    draw_mark(img, (off, off, inner), WHITE + (255,), simplified=simplified, stroke=stroke)
    return img


def write_ico(path):
    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    imgs = [app_icon(s) for s in sizes]
    imgs[-1].save(path, format="ICO", sizes=[(s, s) for s in sizes], append_images=imgs[:-1])


# ---------------------------------------------------------------- installer images
def wizard_large(scale):
    w, h = int(164 * scale), int(314 * scale)
    img = Image.new("RGBA", (w, h), TEAL + (255,))
    m = int(w * 0.56)
    draw_mark(img, ((w - m) // 2 + int(w * 0.02), int(h * 0.28), m), WHITE + (255,))
    d = ImageDraw.Draw(img)
    f1, f2 = font(int(24 * scale)), font(int(16 * scale))
    y = int(h * 0.28) + m + int(14 * scale)
    for text, f, color in (("와이패스", f1, WHITE), ("WiPass", f2, TEAL_LIGHT)):
        tw = d.textlength(text, font=f)
        d.text(((w - tw) / 2, y), text, font=f, fill=color)
        y += int(f.size * 1.35)
    return img.convert("RGB")


def wizard_small(scale):
    s = int(55 * scale)
    img = Image.new("RGBA", (s, s), WHITE + (255,))
    m = int(s * 0.80)
    draw_mark(img, ((s - m) // 2, (s - m) // 2, m), TEAL + (255,))
    return img.convert("RGB")


# ---------------------------------------------------------------- banner / social preview
def banner(w, h, centered):
    img = Image.new("RGBA", (w, h), MINT_BG + (255,))
    d = ImageDraw.Draw(img)
    title, latin, tag = font(int(h * (0.20 if centered else 0.26))), font(int(h * (0.15 if centered else 0.19))), font(int(h * (0.065 if centered else 0.085)), bold=False)
    m = int(h * (0.34 if centered else 0.52))
    t1, t2 = "와이패스", "WiPass"
    tagline = "같은 이름 학교 와이파이, 장소에 맞게 자동 전환"
    gap = int(h * 0.04)
    t1w, t2w = d.textlength(t1, font=title), d.textlength(t2, font=latin)
    if centered:
        draw_mark(img, ((w - m) // 2, int(h * 0.12), m), TEAL + (255,))
        y = int(h * 0.12) + m + int(h * 0.04)
        total = t1w + gap + t2w
        x = (w - total) / 2
        d.text((x, y), t1, font=title, fill=TEAL_DARK)
        d.text((x + t1w + gap, y + (title.size - latin.size) * 0.55), t2, font=latin, fill=TEAL)
        y += int(title.size * 1.45)
        tw = d.textlength(tagline, font=tag)
        d.text(((w - tw) / 2, y), tagline, font=tag, fill=INK)
    else:
        x = int(h * 0.30)
        draw_mark(img, (x, (h - m) // 2, m), TEAL + (255,))
        x += m + int(h * 0.14)
        y = int(h * 0.24)
        d.text((x, y), t1, font=title, fill=TEAL_DARK)
        d.text((x + t1w + gap, y + (title.size - latin.size) * 0.55), t2, font=latin, fill=TEAL)
        d.text((x, y + int(title.size * 1.40)), tagline, font=tag, fill=INK)
    return img.convert("RGB")


def main():
    out = lambda *p: os.path.join(ROOT, *p)
    write_ico(out("src", "WiPass", "wipass.ico"))
    for pct, scale in ((100, 1.0), (150, 1.5), (200, 2.0)):
        wizard_large(scale).save(out("installer", "wizard-large-%d.bmp" % pct))
        wizard_small(scale).save(out("installer", "wizard-small-%d.bmp" % pct))
    os.makedirs(out("docs", "images"), exist_ok=True)
    banner(1280, 320, centered=False).save(out("docs", "images", "banner.png"), optimize=True)
    banner(1280, 640, centered=True).save(out("docs", "images", "social-preview.png"), optimize=True)
    # previews for review (not committed)
    prev = out("tools", "brand", "preview")
    os.makedirs(prev, exist_ok=True)
    sheet = Image.new("RGBA", (560, 120), (246, 247, 249, 255))
    x = 10
    for s in (16, 24, 32, 48, 64, 96):
        sheet.alpha_composite(app_icon(s), (x, 60 - s // 2))
        x += s + 24
    sheet.convert("RGB").save(os.path.join(prev, "icons.png"))
    print("done")


if __name__ == "__main__":
    main()
