"""Store listing images: poster, box art, app tile and screenshot backdrops (Turkish and English).

Usage (from the repo root, needs Pillow):
    python tools/Packaging/make_listing_images.py <out-dir> [path/to/logo.png]

Writes, for each language in LANGS:
    <out>/<lang>/logos/poster-720x1080.png, poster-1440x2160.png   (9:16 poster art)
    <out>/<lang>/logos/boxart-1080x1080.png, boxart-2160x2160.png  (1:1 box art)
    <out>/<lang>/logos/apptile-300x300.png                         (1:1 app tile icon)
    <out>/_backdrops/<lang>-<shot>.png                             (1920x1080 screenshot backgrounds)
The screenshots themselves are captured by Capture-StoreScreenshots.ps1 on top of the backdrops.
The logo lives in do_not_share/ and is never committed.
"""

import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = Path(__file__).resolve().parents[2]
OUT = Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / "dist" / "store" / "listing"
SOURCE = Path(sys.argv[2]) if len(sys.argv) > 2 else ROOT / "do_not_share" / "logo.png"
FONTS = Path("C:/Windows/Fonts")

LANGS = ("tr", "en")

TAGLINE = {
    "tr": ("Çalan şarkının sözleri,", "şarkıyla birlikte."),
    "en": ("The lyrics of whatever", "you're playing, in sync."),
}

# Screenshot captions: (headline, subline). Keys match Capture-StoreScreenshots.ps1.
CAPTIONS = {
    "tr": {
        "1-lyrics": ("Çalan şarkının sözleri", "Spotify, YouTube ve Windows medya kontrollerindeki her uygulama"),
        "2-focus": ("Karaoke odak", "Yalnızca söylenen satır ve çevresi görünür"),
        "3-floating": ("Masaüstünde yüzen sözler", "Saydam mod: harflerin dışına tıklayınca arkadaki pencereye geçer"),
        "4-style": ("Size göre görünüm", "Yazı tipi, renk, gölge ve hazır stiller"),
    },
    "en": {
        "1-lyrics": ("Lyrics for whatever's playing", "Spotify, YouTube and any app in the Windows media controls"),
        "2-focus": ("Karaoke focus", "Only the line being sung and its neighbours stay visible"),
        "3-floating": ("Lyrics that float on your desktop", "Transparent mode: clicks that miss the letters go right through"),
        "4-style": ("Make it yours", "Fonts, colours, shadows and ready-made styles"),
    },
}

TEXT = (232, 230, 226)
SUBTEXT = (170, 168, 164)


def font(name: str, size: int) -> ImageFont.FreeTypeFont:
    return ImageFont.truetype(str(FONTS / name), size)


def texture(logo: Image.Image, size: tuple[int, int]) -> Image.Image:
    """The logo's dark stone background, stretched to cover `size` (the note and text sit in the middle)."""
    w, h = logo.size
    corner = logo.crop((0, 0, w // 3, h // 3))  # plain textured corner, no note/text in it
    scale = max(size[0] / corner.width, size[1] / corner.height)
    tile = corner.resize((round(corner.width * scale) + 1, round(corner.height * scale) + 1), Image.Resampling.LANCZOS)
    # Upscaling makes the grain blocky; a light blur keeps it reading as a soft stone surface.
    return tile.crop((0, 0, size[0], size[1])).filter(ImageFilter.GaussianBlur(max(1.0, scale * 0.6)))


def vignette(img: Image.Image, strength: float = 0.55) -> Image.Image:
    w, h = img.size
    mask = Image.new("L", (w, h), 0)
    ImageDraw.Draw(mask).ellipse((-w * 0.25, -h * 0.25, w * 1.25, h * 1.25), fill=255)
    mask = mask.filter(ImageFilter.GaussianBlur(min(w, h) / 5))
    dark = Image.new("RGB", (w, h), (0, 0, 0))
    return Image.composite(img, Image.blend(img, dark, strength), mask)


def centered_text(draw: ImageDraw.ImageDraw, y: int, text: str, fnt, fill, width: int) -> None:
    box = draw.textbbox((0, 0), text, font=fnt)
    draw.text(((width - (box[2] - box[0])) / 2 - box[0], y), text, font=fnt, fill=fill)


def poster(logo: Image.Image, lang: str, size: tuple[int, int]) -> Image.Image:
    w, h = size
    img = vignette(texture(logo, size))
    # Full logo (note + "MrW Lyrics") in the upper part, edges feathered into the texture.
    side = round(w * 0.98)
    front = logo.resize((side, side), Image.Resampling.LANCZOS)
    mask = Image.new("L", (side, side), 0)
    ImageDraw.Draw(mask).ellipse((side * 0.04, side * 0.04, side * 0.96, side * 0.96), fill=255)
    mask = mask.filter(ImageFilter.GaussianBlur(side / 14))
    top = round(h * 0.12)
    img.paste(front, ((w - side) // 2, top), mask)

    draw = ImageDraw.Draw(img)
    fnt = font("seguisb.ttf", round(w * 0.068))
    line1, line2 = TAGLINE[lang]
    y = top + side + round(h * 0.03)
    centered_text(draw, y, line1, fnt, TEXT, w)
    centered_text(draw, y + round(fnt.size * 1.3), line2, fnt, TEXT, w)
    return img


def boxart(logo: Image.Image, px: int) -> Image.Image:
    return logo.resize((px, px), Image.Resampling.LANCZOS)


def backdrop(logo: Image.Image, lang: str, shot: str) -> Image.Image:
    """1920x1080 screenshot background: stone texture, caption on the left, small brand mark."""
    w, h = 1920, 1080
    img = texture(logo, (w, h))
    if shot == "3-floating":
        # Floating lyrics need something "desktop-like" behind them: a warm, blurred glow.
        glow = Image.new("RGB", (w, h), (0, 0, 0))
        g = ImageDraw.Draw(glow)
        g.ellipse((700, 150, 1900, 1050), fill=(120, 70, 160))
        g.ellipse((1100, 380, 2100, 1200), fill=(40, 110, 150))
        glow = glow.filter(ImageFilter.GaussianBlur(160))
        img = Image.blend(img, glow, 0.55)
    img = vignette(img, 0.45)

    draw = ImageDraw.Draw(img)
    head, sub = CAPTIONS[lang][shot]
    x = 110
    head_font = font("seguisb.ttf", 64)
    sub_font = font("segoeui.ttf", 30)
    # Wrap the headline to the caption column (left ~40% of the image).
    column = {"3-floating": 1700, "4-style": 540}.get(shot, 640)
    y = 120
    for line in wrap(draw, head, head_font, column):
        draw.text((x, y), line, font=head_font, fill=TEXT)
        y += 80
    y += 16
    for line in wrap(draw, sub, sub_font, column):
        draw.text((x, y), line, font=sub_font, fill=SUBTEXT)
        y += 42

    # Brand mark bottom-left: the note + name.
    note_box = (round(0.195 * logo.width), round(0.078 * logo.height), round(0.817 * logo.width), round(0.700 * logo.height))
    note = logo.crop(note_box).resize((72, 72), Image.Resampling.LANCZOS)
    img.paste(note, (x, h - 130))
    draw.text((x + 88, h - 116), "MrW Lyrics", font=font("seguisb.ttf", 34), fill=TEXT)
    return img


def wrap(draw: ImageDraw.ImageDraw, text: str, fnt, width: int) -> list[str]:
    words, lines, current = text.split(), [], ""
    for word in words:
        candidate = f"{current} {word}".strip()
        if draw.textlength(candidate, font=fnt) <= width or not current:
            current = candidate
        else:
            lines.append(current)
            current = word
    if current:
        lines.append(current)
    return lines


def main() -> None:
    logo = Image.open(SOURCE).convert("RGB")
    backdrops = OUT / "_backdrops"
    backdrops.mkdir(parents=True, exist_ok=True)
    for lang in LANGS:
        logos = OUT / lang / "logos"
        logos.mkdir(parents=True, exist_ok=True)
        poster(logo, lang, (720, 1080)).save(logos / "poster-720x1080.png", optimize=True)
        poster(logo, lang, (1440, 2160)).save(logos / "poster-1440x2160.png", optimize=True)
        boxart(logo, 1080).save(logos / "boxart-1080x1080.png", optimize=True)
        boxart(logo, 2160).save(logos / "boxart-2160x2160.png", optimize=True)
        boxart(logo, 300).save(logos / "apptile-300x300.png", optimize=True)
        for shot in CAPTIONS[lang]:
            backdrop(logo, lang, shot).save(backdrops / f"{lang}-{shot}.png", optimize=True)
    print(f"listing images in {OUT}")


if __name__ == "__main__":
    main()
