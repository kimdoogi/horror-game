"""Builds every Steam capsule from an in-game render plus a title treatment.

The sizes, and where each one came from
---------------------------------------
Read off Steamworks' own documentation on 2026-08-01 rather than recalled. Valve
has changed these before — the header capsule was 460x215 until the library
redesign — so each entry below carries the page it was read from, and the page is
the authority the moment it disagrees with this file.

  store    Store Graphical Assets
           https://partner.steamgames.com/doc/store/assets/standard
  library  Steam Library Assets
           https://partner.steamgames.com/doc/store/assets/libraryassets
  index    Graphical Asset Overview
           https://partner.steamgames.com/doc/store/assets

The one rule that decides the design
------------------------------------
"Small Capsule should contain readable logo, even at smallest size... In most
cases, this means your logo should nearly fill the small capsule" (store). The
462x174 small capsule is auto-reduced by Steam to 184x69 and 120x45, so the game's
name is rendered at roughly a quarter of its authored height in the place it is
seen most. Everything else in the design gives way to that: --check writes the
reduced sizes so the claim can be looked at rather than assumed.

Valve also rejects capsules carrying text beyond the title — no review scores, no
"Wishlist now", no platform logos — and the library hero must carry no text at
all, because the library logo is composited over it at a position chosen on the
partner site.

Usage
-----
    python3 tools/render/store_capsules.py                    # the whole set
    python3 tools/render/store_capsules.py --variant latin    # Latin-first lockup
    python3 tools/render/store_capsules.py --check            # + legibility proofs
"""

import argparse
import os
import sys

from PIL import Image, ImageDraw, ImageFilter, ImageFont

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SHOTS = os.path.join(REPO, "docs", "store", "screenshots")
OUT = os.path.join(REPO, "docs", "store", "capsules")

# (name, width, height, source page, what it is for)
CAPSULES = [
    ("header_capsule",   920,  430, "store",   "top of the store page, search results, most lists"),
    ("small_capsule",    462,  174, "store",   "search suggestions and every compact list; auto-reduced to 184x69 and 120x45"),
    ("main_capsule",    1232,  706, "store",   "front-page featured carousel and daily deals"),
    ("vertical_capsule", 748,  896, "store",   "seasonal sale pages and Featured & Recommended"),
    ("page_background", 1438,  810, "store",   "store page backdrop; optional, derived from a screenshot if omitted"),
    ("library_capsule",  600,  900, "library", "the player's own library grid; half-size 300x450 auto-generated"),
    ("library_header",   920,  430, "library", "library detail header"),
    ("library_hero",    3840, 1240, "library", "wide library banner; NO TEXT, 860x380 centre stays uncropped"),
    ("library_logo",    1280,  720, "library", "transparent PNG logotype, composited over the hero"),
    ("community_icon",   184,  184, "index",   "community hub app icon"),
    ("shortcut_icon",    256,  256, "index",   "desktop shortcut icon"),
]

# Which render feeds which capsule. Chosen for what survives a crop, not for what
# looks best at full size: the creature's face and the beam pool are the only two
# things in this game that read at 120x45.
SOURCES = {
    "header_capsule":   "04_the_glance_back.png",
    "small_capsule":    "04_the_glance_back.png",
    "main_capsule":     "04_the_glance_back.png",
    "vertical_capsule": "04_the_glance_back.png",
    "page_background":  "02_the_monster_at_distance.png",
    "library_capsule":  "04_the_glance_back.png",
    "library_header":   "04_the_glance_back.png",
    "library_hero":     "01_corridor_and_beam.png",
    "community_icon":   "04_the_glance_back.png",
    "shortcut_icon":    "04_the_glance_back.png",
}

KOREAN = "/System/Library/Fonts/AppleSDGothicNeo.ttc"
KOREAN_BOLD = 6            # face index; see ImageFont.truetype(..., index=)
LATIN = "/System/Library/Fonts/Avenir Next Condensed.ttc"
LATIN_HEAVY = 8

# §07's night is cold and §12's practicals are the only warmth in the building.
# The capsule keeps that palette rather than inventing a marketing one.
INK = (238, 236, 232)
EMBER = (214, 122, 74)


def load_source(name):
    path = os.path.join(SHOTS, SOURCES[name])
    if not os.path.exists(path):
        raise SystemExit("missing render: " + path + " — run tools/render/store_shots.py first")
    return Image.open(path).convert("RGB")


def cover(image, width, height, focus_x=0.5, focus_y=0.5):
    """Scales to fill and crops, keeping a chosen point in view.

    Steam crops capsules further at some sizes, so nothing load-bearing may sit in
    the outer tenth; the focus point is how the creature is kept off that edge.
    """
    scale = max(width / image.width, height / image.height)
    scaled = image.resize(
        (max(width, int(round(image.width * scale))), max(height, int(round(image.height * scale)))),
        Image.LANCZOS)
    left = int(round((scaled.width - width) * focus_x))
    top = int(round((scaled.height - height) * focus_y))
    return scaled.crop((left, top, left + width, top + height))


def scrim(image, strength=0.55, direction="bottom"):
    """Darkens one end of the frame so a light logotype has something to sit on.

    Without it the title lands on whatever the render happened to put there, and
    "legible at 120x45" becomes a property of the screenshot rather than of the
    design.
    """
    width, height = image.size
    mask = Image.new("L", (width, height), 0)
    draw = ImageDraw.Draw(mask)

    for i in range(height):
        t = i / max(1, height - 1)
        if direction == "bottom":
            a = t ** 1.6
        elif direction == "top":
            a = (1.0 - t) ** 1.6
        else:
            a = 1.0
        draw.line([(0, i), (width, i)], fill=int(255 * a * strength))

    black = Image.new("RGB", (width, height), (0, 0, 0))
    return Image.composite(black, image, mask)


def fitted(font_path, index, text, target_width, target_height, start=400):
    """Largest size at which the text fits both bounds. Titles are set by the box."""
    size = start
    while size > 8:
        font = ImageFont.truetype(font_path, size, index=index)
        box = font.getbbox(text)
        if box[2] - box[0] <= target_width and box[3] - box[1] <= target_height:
            return font
        size -= 2
    return ImageFont.truetype(font_path, 8, index=index)


def draw_title(image, title, subtitle, variant, tight=False):
    """Lays the name over the frame, with a shadow that survives the reduction.

    A blurred dark copy under the glyphs is what keeps the logotype off the
    background at 120x45, where antialiasing has eaten most of the stroke.
    """
    width, height = image.size
    layer = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    draw = ImageDraw.Draw(layer)

    if variant == "latin":
        main, second = subtitle, title
        main_font_path, main_index = LATIN, LATIN_HEAVY
        second_font_path, second_index = KOREAN, KOREAN_BOLD
    else:
        main, second = title, subtitle
        main_font_path, main_index = KOREAN, KOREAN_BOLD
        second_font_path, second_index = LATIN, LATIN_HEAVY

    # "Your logo should nearly fill the small capsule" — so the name is sized off
    # the capsule, not off a fixed point size, and it takes most of the width.
    fill = 0.90 if tight else 0.80
    font = fitted(main_font_path, main_index, main,
                  int(width * fill), int(height * (0.46 if tight else 0.30)))
    box = font.getbbox(main)
    tw, th = box[2] - box[0], box[3] - box[1]

    second_font = None
    sw = sh = 0
    if second and not tight:
        spaced = " ".join(second)
        second_font = fitted(second_font_path, second_index, spaced,
                             int(width * 0.62), max(10, int(height * 0.070)))
        sbox = second_font.getbbox(spaced)
        sw, sh = sbox[2] - sbox[0], sbox[3] - sbox[1]
        second = spaced

    gap = int(height * 0.045)
    total = th + (gap + sh if second_font else 0)
    x = (width - tw) // 2
    y = int(height * (0.50 if tight else 0.62)) - total // 2

    draw.text((x - box[0], y - box[1]), main, font=font, fill=INK + (255,))
    if second_font:
        draw.text(((width - sw) // 2 - second_font.getbbox(second)[0],
                   y + th + gap - second_font.getbbox(second)[1]),
                  second, font=second_font, fill=EMBER + (255,))

    # A blurred black copy of the glyph alpha, laid under the glyphs. This is what
    # keeps the logotype off the background at 120x45, where the reduction has
    # eaten most of the stroke and a thin outline would disappear with it.
    halo = layer.split()[3].filter(ImageFilter.GaussianBlur(max(2, width // 120)))
    halo = halo.point(lambda a: min(255, int(a * 2.6)))
    shadow = Image.merge("RGBA", (
        Image.new("L", (width, height), 0),
        Image.new("L", (width, height), 0),
        Image.new("L", (width, height), 0),
        halo))

    out = image.convert("RGBA")
    out.alpha_composite(shadow)
    out.alpha_composite(layer)
    return out.convert("RGB")


def build(name, width, height, title, subtitle, variant):
    if name == "library_logo":
        # "This image should contain only your game's logo type... on transparent
        # background" (library). No frame, no scrim, nothing else.
        canvas = Image.new("RGBA", (width, height), (0, 0, 0, 0))
        block = draw_title(Image.new("RGB", (width, height), (0, 0, 0)), title, subtitle, variant)
        alpha = block.convert("L").point(lambda v: 255 if v > 26 else int(v * 9.8))
        canvas = Image.merge("RGBA", block.split() + (alpha,))
        return canvas

    source = load_source(name)

    if name in ("community_icon", "shortcut_icon"):
        # The creature's face is the only mark in this game that reads at 32 px.
        frame = cover(source, width, height, focus_x=0.86, focus_y=0.42)
        return scrim(frame, 0.22, "none")

    if name == "library_hero":
        # "This image cannot include any text" (library), and the centre 860x380
        # is the part that survives every crop, so it is kept empty of incident.
        return scrim(cover(source, width, height, focus_y=0.55), 0.30, "top")

    if name == "page_background":
        # Sits behind the whole store page under Valve's own darkening, so it
        # carries no title either — it would collide with the page's content.
        return scrim(cover(source, width, height), 0.45, "top")

    # The portrait capsules crop hard, so they are pulled onto the creature:
    # at 300x450 a monster 15 m down a corridor is four pixels of nothing.
    # Valve crops capsules further at some sizes, so nothing load-bearing may sit
    # in the outer tenth. 748x896 is the wider of the two portraits and needs a
    # gentler pull, or the creature's face lands on the trim edge.
    focus_x = {"vertical_capsule": 0.70, "library_capsule": 0.80}.get(name, 0.5)
    frame = cover(source, width, height, focus_x=focus_x)
    frame = scrim(frame, 0.62 if height > 300 else 0.72)
    return draw_title(frame, title, subtitle, variant, tight=(name == "small_capsule"))


def check(name, image):
    """Writes the reductions Steam generates, so "legible at size" can be looked at."""
    proofs = os.path.join(OUT, "legibility")
    os.makedirs(proofs, exist_ok=True)

    if name == "small_capsule":
        sizes = [(184, 69), (120, 45)]
    elif name == "main_capsule":
        sizes = [(374, 214)]          # ~30%, the size the front page actually uses
    elif name == "header_capsule":
        sizes = [(292, 136)]
    elif name == "library_capsule":
        sizes = [(300, 450)]
    else:
        return

    for w, h in sizes:
        image.resize((w, h), Image.LANCZOS).save(
            os.path.join(proofs, "%s_%dx%d.png" % (name, w, h)))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--title", default="요양원 지하",
                        help="the name as the in-game main menu sets it")
    parser.add_argument("--subtitle", default="SANATORIUM BELOW",
                        help="the Latin rendering; provisional until the owner picks one")
    parser.add_argument("--variant", choices=("korean", "latin"), default="korean")
    parser.add_argument("--check", action="store_true", help="also write the reduced proofs")
    args = parser.parse_args()

    os.makedirs(OUT, exist_ok=True)
    for name, width, height, source, purpose in CAPSULES:
        image = build(name, width, height, args.title, args.subtitle, args.variant)
        path = os.path.join(OUT, "%s_%dx%d.png" % (name, width, height))
        image.save(path)
        if args.check:
            check(name, image.convert("RGB"))
        print("%-18s %5dx%-5d %-8s %s" % (name, width, height, source, purpose))

    print("\n%d assets in %s" % (len(CAPSULES), os.path.relpath(OUT, REPO)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
