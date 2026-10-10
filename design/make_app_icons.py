"""Builds the Android launcher icons and the splash logo from design/gheychi-icon.svg.

The svg is a rounded square (its first path) with the speech bubble and scissors on top. Android masks adaptive icons
into circles, squircles and so on, so there the square is the background colour and the mark alone is the foreground,
sized to fit the safe zone (a circle 61% of the canvas wide). Before Android 8 the icon is used as it is: the rounded
square, or the mark on a circle where the launcher asks for a round one.

    pip install resvg-py pillow
    python design/make_app_icons.py
"""
import io
import math
import re
from pathlib import Path

from PIL import Image, ImageDraw
from resvg_py import svg_to_bytes

root = Path(__file__).resolve().parent.parent
source = (root / "design/gheychi-icon.svg").read_text(encoding="utf-8")
res = root / "src/Gheychi.App/Resources"

# Mark and square sit in the file's own coordinates: a group offset and then one transform per path.
view = [float(v) for v in re.search(r'viewBox="([^"]+)"', source).group(1).split()]
group = re.search(r"<g[^>]*>", source).group(0)
square, *mark = re.findall(r"<path .*?/>", source, re.S)
background = re.search(r'fill="(#[0-9A-Fa-f]{6})"', square).group(1).upper()

SAFE_RADIUS = 0.305  # of the adaptive canvas, 66dp of 108dp across
ROUND_RADIUS = 0.42  # of a legacy round icon: the mark sits well inside the circle

DENSITIES = {"mdpi": 1, "hdpi": 1.5, "xhdpi": 2, "xxhdpi": 3, "xxxhdpi": 4}


def render(paths, box, size):
    x, y, w, h = box
    svg = (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="{x} {y} {w} {h}" width="{size}" height="{size}">'
           f'{group}{"".join(paths)}</g></svg>')
    return Image.open(io.BytesIO(bytes(svg_to_bytes(svg_string=svg, width=size, height=size)))).convert("RGBA")


def extent(paths):
    """The centre of the paths' bounding box and the distance of their farthest pixel from it, in file units."""
    margin = 300
    wide = (view[0] - margin, view[1] - margin, view[2] + 2 * margin, view[3] + 2 * margin)
    size = round(wide[2])
    img = render(paths, wide, size)
    alpha = img.getchannel("A").point(lambda a: 255 if a > 8 else 0)
    left, top, right, bottom = alpha.getbbox()
    cx, cy = (left + right) / 2, (top + bottom) / 2
    pixels = alpha.load()
    far = max(math.hypot(x - cx, y - cy) for y in range(top, bottom) for x in range(left, right) if pixels[x, y])
    return (wide[0] + cx * wide[2] / size, wide[1] + cy * wide[3] / size), far * wide[2] / size, (
        wide[0] + left * wide[2] / size, wide[1] + top * wide[3] / size,
        wide[0] + right * wide[2] / size, wide[1] + bottom * wide[3] / size)


(mark_x, mark_y), mark_radius, _ = extent(mark)
_, _, (sq_left, sq_top, sq_right, sq_bottom) = extent([square])
sq_size = max(sq_right - sq_left, sq_bottom - sq_top)
sq_box = ((sq_left + sq_right) / 2 - sq_size / 2, (sq_top + sq_bottom) / 2 - sq_size / 2, sq_size, sq_size)


def mark_box(radius):
    """The box that puts the mark's centre in the middle with its farthest point `radius` of the width from it."""
    side = mark_radius / radius
    return mark_x - side / 2, mark_y - side / 2, side, side


def save(img, folder, name):
    folder = res / folder
    folder.mkdir(parents=True, exist_ok=True)
    img.save(folder / name, optimize=True)


for density, scale in DENSITIES.items():
    legacy = round(48 * scale)
    adaptive = round(108 * scale)
    save(render([square, *mark], sq_box, legacy), f"mipmap-{density}", "appicon.png")
    save(render(mark, mark_box(SAFE_RADIUS), adaptive), f"mipmap-{density}", "appicon_foreground.png")

    disc = Image.new("RGBA", (legacy * 4, legacy * 4))
    ImageDraw.Draw(disc).ellipse((0, 0, legacy * 4 - 1, legacy * 4 - 1), fill=background)
    round_icon = Image.alpha_composite(disc.resize((legacy, legacy), Image.LANCZOS), render(mark, mark_box(ROUND_RADIUS), legacy))
    save(round_icon, f"mipmap-{density}", "appicon_round.png")

save(render(mark, mark_box(SAFE_RADIUS), 432), "drawable-nodpi", "splash_logo.png")

colour_file = res / "values/appicon_background.xml"
colours = colour_file.read_bytes()
colour_file.write_bytes(re.sub(rb'(<color name="appicon_background">)#[0-9A-Fa-f]{6}', rb"\g<1>" + background.encode(), colours))
print(f"wrote launcher icons and splash logo, background {background}")
