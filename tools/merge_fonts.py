"""
Builds the Persian-mode UI fonts: Plus Jakarta Sans letters and digits on top of Vazirmatn.

Avalonia's font fallback is one global list with no weight, and each weight here is its own family, so a fallback from
Plus Jakarta Sans SemiBold to Vazirmatn would draw bold Persian text in Regular. One merged file per weight avoids that.
Vertical metrics stay Vazirmatn's, so lines are as tall as they were with Vazirmatn alone.

Usage: python tools/merge_fonts.py   (needs fonttools)
"""
import tempfile
from pathlib import Path

from fontTools import subset
from fontTools.merge import Merger
from fontTools.ttLib import TTFont
from fontTools.ttLib.scaleUpem import scale_upem

FONTS = Path(__file__).resolve().parent.parent / "src/Gheychi.App/UiAssets/Fonts"
FAMILY = "Gheychi Fa"

# What Plus Jakarta Sans draws: Basic Latin, Latin-1, Latin Extended-A, general punctuation, currency, arrows.
LATIN = [(0x20, 0x7E), (0xA0, 0x17F), (0x2000, 0x206F), (0x20A0, 0x20CF), (0x2190, 0x21FF)]


# The word space stays Vazirmatn's: Plus Jakarta Sans' is a third narrower, which crowds Persian words together.
KEEP_VAZIRMATN = {0x20, 0xA0}


def in_latin(cp):
    return cp not in KEEP_VAZIRMATN and any(lo <= cp <= hi for lo, hi in LATIN)


def subset_font(font, keep):
    options = subset.Options()
    options.layout_features = ["*"]
    options.glyph_names = False
    options.notdef_outline = True
    options.name_IDs = ["*"]
    options.name_languages = ["*"]
    options.hinting = False
    subsetter = subset.Subsetter(options)
    subsetter.populate(unicodes=keep)
    subsetter.subset(font)
    return font


def build(weight):
    latin = TTFont(FONTS / f"PlusJakartaSans-{weight}.ttf")
    persian = TTFont(FONTS / f"Vazirmatn-{weight}.ttf")
    metrics = TTFont(FONTS / f"Vazirmatn-{weight}.ttf")

    scale_upem(latin, persian["head"].unitsPerEm)
    subset_font(latin, [cp for cp in latin.getBestCmap() if in_latin(cp)])
    subset_font(persian, [cp for cp in persian.getBestCmap() if not in_latin(cp)])

    with tempfile.TemporaryDirectory() as tmp:
        a, b = Path(tmp) / "latin.ttf", Path(tmp) / "persian.ttf"
        latin.save(a)
        persian.save(b)
        merged = Merger().merge([str(a), str(b)])

    for table, names in (
        ("hhea", ("ascent", "descent", "lineGap")),
        ("OS/2", ("sTypoAscender", "sTypoDescender", "sTypoLineGap", "usWinAscent", "usWinDescent")),
    ):
        for name in names:
            setattr(merged[table], name, getattr(metrics[table], name))
    merged["OS/2"].usWeightClass = metrics["OS/2"].usWeightClass
    merged["OS/2"].fsSelection = metrics["OS/2"].fsSelection

    family = FAMILY if weight == "Regular" else f"{FAMILY} {weight}"
    ps = family.replace(" ", "") + ("-Regular" if weight == "Regular" else "")
    name = merged["name"]
    name.names = [n for n in name.names if n.nameID not in (1, 2, 3, 4, 6, 16, 17, 21, 22, 25)]
    for platform, enc, lang in ((3, 1, 0x409), (1, 0, 0)):
        name.setName(family, 1, platform, enc, lang)
        name.setName("Regular", 2, platform, enc, lang)
        name.setName(f"{family} {weight}" if weight == "Regular" else family, 4, platform, enc, lang)
        name.setName(ps, 6, platform, enc, lang)
        name.setName(f"{ps};merged", 3, platform, enc, lang)

    out = FONTS / f"GheychiFa-{weight}.ttf"
    merged.save(out)
    print(out.name, out.stat().st_size)


if __name__ == "__main__":
    for w in ("Regular", "SemiBold"):
        build(w)
