"""Builds the app's launcher icon layers and splash image from design/gheychi-icon.svg.

Android masks launcher icons into circles, squircles and so on, so the mark sits inside the adaptive icon's safe
zone (a circle 61% of the canvas wide) and the background is a full-bleed square. The renderer behind MAUI's
resizetizer does not resolve <use>, so every shape is written out in full.

    python design/make_app_icons.py
"""
import re
from pathlib import Path

root = Path(__file__).resolve().parent.parent
design = (root / "design/gheychi-icon.svg").read_text(encoding="utf-8")
out_icon = root / "src/Gheychi.App/Resources/AppIcon"
out_splash = root / "src/Gheychi.App/Resources/Splash"

# The bubble's farthest point is 309 units from its centre (400, 406.5); 309 / 0.305 ~ 1013.
SIZE = 1020
VIEWBOX = f"{400 - SIZE / 2:g} {406.5 - SIZE / 2:g} {SIZE} {SIZE}"
SPLASH_BACKGROUND = "#05190F"


def gradient(name):
    return re.search(rf'<linearGradient id="{name}".*?</linearGradient>', design, re.S).group(0)


def path_d(name):
    return " ".join(re.search(rf'<path id="{name}"[^>]*?d="([^"]+)"', design, re.S).group(1).split())


def mirrored(d):
    return re.sub(r"(-?\d+(?:\.\d+)?),(-?\d+(?:\.\d+)?)", lambda m: f"{m.group(1)},{740 - float(m.group(2)):g}", d)


bubble = " ".join(re.search(r'<path fill="#0b2c1d".*?d="([^"]+)"', design, re.S).group(1).split())
loop, blade_a, blade_b, tip_a = path_d("loop"), path_d("bladeA"), path_d("bladeB"), path_d("bladeTipA")
loop_b = mirrored(loop)


def mark(cutout):
    """The bubble and scissors. `cutout` paints the gap between the halves and the pivot ring: the background's own
    gradient on the launcher icon, so the layers line up, and a flat colour on the splash screen."""
    return f"""
  <path fill="#0b2c1d" fill-opacity="0.45" stroke="url(#bubble)" stroke-width="13" stroke-linejoin="round" d="{bubble}"/>
  <g fill="url(#halfA)">
    <path fill-rule="evenodd" d="{loop}"/>
    <path d="{blade_a}"/>
    <circle cx="531" cy="481" r="16"/>
  </g>
  <g fill="url(#bladeShade)">
    <path d="{tip_a}"/>
    <circle cx="531" cy="481" r="16"/>
  </g>
  <g fill="{cutout}" stroke="{cutout}" stroke-width="16" stroke-linejoin="round">
    <path d="{loop_b}"/>
    <path d="{blade_b}"/>
    <circle cx="535" cy="250" r="16"/>
  </g>
  <g fill="url(#halfB)">
    <path fill-rule="evenodd" d="{loop_b}"/>
    <path d="{blade_b}"/>
    <circle cx="535" cy="250" r="16"/>
  </g>
  <circle cx="410" cy="364" r="17" fill="{cutout}"/>
  <circle cx="410" cy="364" r="8.5" fill="url(#pin)"/>"""


def svg(defs, body):
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="{VIEWBOX}" width="456" height="456">\n'
            f'  <defs>\n    {"".join(defs)}\n  </defs>{body}\n</svg>\n')


mark_gradients = [gradient(n) for n in ("bubble", "halfA", "halfB", "bladeShade", "pin")]
x0, y0 = 400 - SIZE / 2, 406.5 - SIZE / 2
(out_icon / "appicon.svg").write_text(svg([gradient("bg")], f'\n  <rect x="{x0:g}" y="{y0:g}" width="{SIZE}" height="{SIZE}" fill="url(#bg)"/>'), encoding="utf-8")
(out_icon / "appiconfg.svg").write_text(svg([gradient("bg")] + mark_gradients, mark("url(#bg)")), encoding="utf-8")
(out_splash / "splash.svg").write_text(svg(mark_gradients, mark(SPLASH_BACKGROUND)), encoding="utf-8")
print("wrote appicon.svg, appiconfg.svg, splash.svg")
