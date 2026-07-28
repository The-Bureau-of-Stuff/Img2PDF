"""Generates the full Scanstack MSIX icon asset set from a single vector definition.

Run:  python tools/generate-icons.py
Writes to src/Img2PDF.Package/Images/ and tools/out/ (store + .ico extras).

The mark is a fanned stack of three sheets with a red badge on the front sheet.
Two treatments are produced:
  - "tiled"  : the mark inside a rounded indigo square, transparent outside it.
               Used where the icon renders standalone (app list, taskbar, Store).
  - "glyph"  : the mark alone on transparency. Used for Start tiles, which sit on
               the manifest's BackgroundColor (#31418C).
"""

import math
import os

from PIL import Image, ImageDraw

TILE_COLOR = (0x31, 0x41, 0x8C, 255)
BADGE_COLOR = (0xE0, 0x4A, 0x3F, 255)
MASTER = 2048  # supersampled master; every asset is a LANCZOS downsample of this

HERE = os.path.dirname(os.path.abspath(__file__))
IMAGES_DIR = os.path.join(HERE, "..", "src", "Img2PDF.Package", "Images")
OUT_DIR = os.path.join(HERE, "out")

# Fraction of a tile's short edge occupied by the glyph artboard on Start tiles.
# 0.85 puts the visible mark at ~58% of the tile's short edge, which is the size
# Microsoft's tile asset guidance calls for (88px of a 150px tile at scale-100).
GLYPH_TILE_RATIO = 0.85


def render_master(with_tile):
    """Draws the mark on a 96-unit artboard, rendered at MASTER pixels square."""
    n = MASTER
    u = n / 96.0
    img = Image.new("RGBA", (n, n), (0, 0, 0, 0))

    if with_tile:
        draw = ImageDraw.Draw(img)
        draw.rounded_rectangle([0, 0, n - 1, n - 1], radius=22 * u, fill=TILE_COLOR)

    # Back to front: two ghosted sheets rotated out, then the solid front sheet.
    for angle, alpha in ((15.0, 97), (7.5, 158), (0.0, 255)):
        layer = Image.new("RGBA", (n, n), (0, 0, 0, 0))
        ld = ImageDraw.Draw(layer)
        ld.rounded_rectangle(
            [28 * u, 22 * u, 68 * u, 74 * u], radius=4 * u, fill=(255, 255, 255, alpha)
        )

        if angle == 0.0:
            # Red foot band on the front sheet. Bottom corners follow the sheet's
            # radius, top edge is square — squaring it off is what makes the shape
            # read as a document band rather than a floating pill at 16-24px.
            ld.rounded_rectangle(
                [28 * u, 56 * u, 68 * u, 74 * u], radius=4 * u, fill=BADGE_COLOR
            )
            ld.rectangle([28 * u, 56 * u, 68 * u, 62 * u], fill=BADGE_COLOR)
        else:
            layer = layer.rotate(
                angle, resample=Image.BICUBIC, center=(48 * u, 48 * u)
            )

        img = Image.alpha_composite(img, layer)

    return img


MASTER_TILED = render_master(True)
MASTER_GLYPH = render_master(False)


def square(path, size, treatment):
    master = MASTER_TILED if treatment == "tiled" else MASTER_GLYPH

    if treatment == "tiled":
        out = master.resize((size, size), Image.LANCZOS)
    else:
        art = max(1, round(size * GLYPH_TILE_RATIO))
        out = Image.new("RGBA", (size, size), (0, 0, 0, 0))
        scaled = master.resize((art, art), Image.LANCZOS)
        off = (size - art) // 2
        out.alpha_composite(scaled, (off, off))

    write(out, path)


def wide(path, width, height):
    art = max(1, round(height * GLYPH_TILE_RATIO))
    out = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    scaled = MASTER_GLYPH.resize((art, art), Image.LANCZOS)
    out.alpha_composite(scaled, ((width - art) // 2, (height - art) // 2))
    write(out, path)


def write(img, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    img.save(path, "PNG", optimize=True)
    print("wrote", os.path.relpath(path, os.path.join(HERE, "..")), img.size)


SCALES = (("100", 1.0), ("125", 1.25), ("150", 1.5), ("200", 2.0), ("400", 4.0))
TARGET_SIZES = (16, 24, 32, 48, 256)


def scaled(base, factor):
    return max(1, round(base * factor))


def main():
    img = lambda name: os.path.join(IMAGES_DIR, name)

    # Standalone icon — app list, taskbar, Store. Keeps its own indigo plate.
    for base, name in ((44, "Square44x44Logo"), (50, "StoreLogo")):
        square(img(name + ".png"), base, "tiled")

        for suffix, factor in SCALES:
            square(img("%s.scale-%s.png" % (name, suffix)), scaled(base, factor), "tiled")

    # targetsize variants drive Start's "all apps" list, taskbar, and Alt+Tab.
    # altform-unplated is identical here because the mark supplies its own plate.
    for size in TARGET_SIZES:
        square(img("Square44x44Logo.targetsize-%d.png" % size), size, "tiled")
        square(
            img("Square44x44Logo.targetsize-%d_altform-unplated.png" % size),
            size,
            "tiled",
        )

    # Start tiles — drawn over the manifest BackgroundColor, so glyph only.
    for base, name in (
        (71, "Square71x71Logo"),
        (150, "Square150x150Logo"),
        (310, "Square310x310Logo"),
    ):
        square(img(name + ".png"), base, "glyph")

        for suffix, factor in SCALES:
            square(img("%s.scale-%s.png" % (name, suffix)), scaled(base, factor), "glyph")

    wide(img("Wide310x150Logo.png"), 310, 150)

    for suffix, factor in SCALES:
        wide(
            img("Wide310x150Logo.scale-%s.png" % suffix),
            scaled(310, factor),
            scaled(150, factor),
        )

    # Extras that live outside the package: window/exe icon and the Partner
    # Center listing logo.
    ico_sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    frames = [MASTER_TILED.resize((s, s), Image.LANCZOS) for s in ico_sizes]
    os.makedirs(OUT_DIR, exist_ok=True)
    ico_path = os.path.join(OUT_DIR, "Scanstack.ico")
    frames[-1].save(ico_path, format="ICO", sizes=[(s, s) for s in ico_sizes])
    print("wrote", os.path.relpath(ico_path, os.path.join(HERE, "..")))

    square(os.path.join(OUT_DIR, "store-logo-300x300.png"), 300, "tiled")
    square(os.path.join(OUT_DIR, "store-logo-2160x2160.png"), 2160, "tiled")


if __name__ == "__main__":
    main()
