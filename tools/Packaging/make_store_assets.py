"""Generates the MSIX/Store images and the app icon from the private logo.

Usage (from the repo root, needs Pillow):
    python tools/Packaging/make_store_assets.py [path/to/logo.png]

The source logo lives in do_not_share/ and is never committed; only the derived
PNGs in src/LyricsApp/Assets and src/LyricsApp/LyricsApp.ico are.
Run it again whenever the logo changes.
"""

import sys
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[2]
SOURCE = Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / "do_not_share" / "logo.png"
ASSETS = ROOT / "src" / "LyricsApp" / "Assets"
ICON = ROOT / "src" / "LyricsApp" / "LyricsApp.ico"


# MSIX logo sizes at scale 100; each one is written at the listed scales.
SQUARE_FULL = {"Square150x150Logo": 150, "LargeTile": 310}
SQUARE_NOTE = {"Square44x44Logo": 44, "SmallTile": 71, "StoreLogo": 50}
WIDE = {"Wide310x150Logo": (310, 150), "SplashScreen": (620, 300)}
SCALES = [100, 125, 150, 200, 400]
LARGE_SCALES = [100, 125, 150, 200]  # scale-400 of the full logo would be an upscale and adds megabytes
TARGET_SIZES = [16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256]
ICO_SIZES = [16, 20, 24, 32, 40, 48, 64, 256]


def resized(img: Image.Image, size: tuple[int, int]) -> Image.Image:
    return img.resize(size, Image.Resampling.LANCZOS)


# ---- Small icons (taskbar, Start list, .ico, Store logo) ----
# The logo's dark-grey note on near-black stone disappears on a dark taskbar, and its shading
# blends into the stone so it cannot be cut out cleanly. Small icons use this drawn eighth note
# instead: same brushed-metal colours, transparent background, thin dark rim for light taskbars.

GLYPH_SS = 1024  # supersampled canvas


def _bezier(p0, p1, p2, p3, n=48):
    pts = []
    for i in range(n + 1):
        t = i / n
        mt = 1 - t
        x = mt**3 * p0[0] + 3 * mt**2 * t * p1[0] + 3 * mt * t**2 * p2[0] + t**3 * p3[0]
        y = mt**3 * p0[1] + 3 * mt**2 * t * p1[1] + 3 * mt * t**2 * p2[1] + t**3 * p3[1]
        pts.append((x * GLYPH_SS, y * GLYPH_SS))
    return pts


def _note_mask() -> Image.Image:
    s = GLYPH_SS
    mask = Image.new("L", (s, s), 0)
    d = ImageDraw.Draw(mask)
    d.rectangle((0.535 * s, 0.08 * s, 0.605 * s, 0.74 * s), fill=255)  # stem
    outer = _bezier((0.535, 0.08), (0.76, 0.20), (0.90, 0.40), (0.76, 0.66))  # flag, outer edge
    inner = _bezier((0.76, 0.66), (0.81, 0.47), (0.73, 0.35), (0.605, 0.30))  # flag, inner edge
    d.polygon(outer + inner, fill=255)
    head = Image.new("L", (s, s), 0)
    ImageDraw.Draw(head).ellipse((0.17 * s, 0.62 * s, 0.61 * s, 0.92 * s), fill=255)
    head = head.rotate(22, resample=Image.Resampling.BICUBIC, center=(0.39 * s, 0.77 * s))
    return ImageChops.lighter(mask, head)


def note_glyph(px: int) -> Image.Image:
    s = GLYPH_SS
    mask = _note_mask()
    grad = Image.new("RGB", (1, s))
    top, bottom = (236, 232, 224), (150, 144, 136)  # warm light grey -> darker, like the logo's metal
    for y in range(s):
        t = y / (s - 1)
        grad.putpixel((0, y), tuple(round(a + (b - a) * t) for a, b in zip(top, bottom)))
    rim = mask.filter(ImageFilter.MaxFilter(max(3, (s // px) * 2 + 1)))  # about one output pixel wide
    out = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    out.paste((28, 26, 24, 235), (0, 0), rim)
    out.paste(grad.resize((s, s)).convert("RGBA"), (0, 0), mask)
    out = out.crop(rim.getbbox())
    side = round(max(out.size) * 1.06)
    square = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    square.paste(out, ((side - out.width) // 2, (side - out.height) // 2), out)
    return square.resize((px, px), Image.Resampling.LANCZOS)


def wide(logo: Image.Image, size: tuple[int, int]) -> Image.Image:
    """Full logo centred on a blurred, stretched copy of itself, edges feathered so it blends."""
    w, h = size
    background = resized(logo, (w, w)).crop((0, (w - h) // 2, w, (w - h) // 2 + h))
    background = background.filter(ImageFilter.GaussianBlur(h / 6))
    front = resized(logo, (h, h))
    mask = Image.new("L", (h, h), 255)
    fade = max(1, h // 8)
    for x in range(fade):
        value = round(255 * x / fade)
        for edge in (x, h - 1 - x):
            mask.paste(value, (edge, 0, edge + 1, h))
    background.paste(front, ((w - h) // 2, 0), mask)
    return background


def save(img: Image.Image, name: str) -> None:
    img.convert("RGBA").save(ASSETS / name, optimize=True)


def main() -> None:
    logo = Image.open(SOURCE).convert("RGB")
    ASSETS.mkdir(parents=True, exist_ok=True)
    for old in ASSETS.glob("*.png"):
        old.unlink()

    for scale in SCALES:
        for name, base in SQUARE_FULL.items():
            px = round(base * scale / 100)
            if scale in LARGE_SCALES:
                save(resized(logo, (px, px)), f"{name}.scale-{scale}.png")
        for name, base in SQUARE_NOTE.items():
            px = round(base * scale / 100)
            save(note_glyph(px), f"{name}.scale-{scale}.png")
        for name, (bw, bh) in WIDE.items():
            size = (round(bw * scale / 100), round(bh * scale / 100))
            if scale in LARGE_SCALES:
                save(wide(logo, size), f"{name}.scale-{scale}.png")

    # Taskbar, Start list and Explorer icons: transparent glyph, readable on dark and light taskbars.
    for px in TARGET_SIZES:
        icon = note_glyph(px)
        save(icon, f"Square44x44Logo.targetsize-{px}.png")
        save(icon, f"Square44x44Logo.targetsize-{px}_altform-unplated.png")
        save(icon, f"Square44x44Logo.targetsize-{px}_altform-lightunplated.png")

    # Exe and window icon (taskbar of the unpackaged app). Each size is rendered on its own so small
    # sizes keep a crisp rim. BMP frames: WPF's icon decoder rejects Pillow's PNG-compressed frames.
    frames = [note_glyph(s) for s in ICO_SIZES]
    frames[-1].save(ICON, sizes=[(s, s) for s in ICO_SIZES], bitmap_format="bmp", append_images=frames[:-1])
    print(f"{len(list(ASSETS.glob('*.png')))} PNGs in {ASSETS.relative_to(ROOT)}, icon {ICON.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
