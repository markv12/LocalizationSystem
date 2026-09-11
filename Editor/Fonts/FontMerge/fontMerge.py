"""Builds NotoAll.ttf: one TTF holding just the characters the game ships, each taken
from the first Noto face that covers it.

Driven by the "Rebuild Localization Font" button on LocalizationMasterDatabase, which
writes the character list first. Run it with no arguments to rebuild from that same list.

Requires: python -m pip install fonttools
"""

import argparse
import os
import sys

from fontTools.ttLib import TTFont

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))

# The first entry is the base font -- it supplies the metadata, metrics and .notdef.
FONT_FILES = [
    "NotoSans-Medium.ttf",
    "NotoSansJP-Regular.ttf",
    "NotoSansTC-Regular.ttf",
    "NotoSansSC-Regular.ttf",
    "NotoSansKR-Regular.ttf",
    "NotoSansThai-Regular.ttf",
    "NotoSansArabic-Regular.ttf",
]

DEFAULT_CHARS = os.path.join(SCRIPT_DIR, "FontCharacters.txt")
DEFAULT_FOUND = os.path.join(SCRIPT_DIR, "FontCharactersFound.txt")
DEFAULT_OUT = os.path.join(SCRIPT_DIR, "NotoAll.ttf")

# Layout tables are copied through verbatim from the base font, so their glyph ids stop
# matching once the glyph order is rebuilt. Drop them rather than ship wrong kerning;
# Arabic is pre-shaped into presentation forms by RTLHelper, so GSUB isn't needed either.
DROPPED_TABLES = ("GSUB", "GPOS", "GDEF", "STAT")


def get_dependencies(font, glyph_name, found=None):
    """Recursively find all component glyph names for a composite glyph."""
    if found is None:
        found = set()
    if glyph_name in found or 'glyf' not in font or glyph_name not in font['glyf']:
        return found

    found.add(glyph_name)
    glyph = font['glyf'][glyph_name]
    if glyph.isComposite():
        for component in glyph.components:
            get_dependencies(font, component.glyphName, found)
    return found


def merge_and_subset(chars_path, out_path, found_path, fonts_dir=None):
    if not os.path.exists(chars_path):
        print(f"Error: character list not found: {chars_path}")
        return 1
    with open(chars_path, encoding="utf-8-sig") as f:
        characters = f.read()

    # Anything below 0x20 is a control character TMP has no glyph for anyway.
    needed_unicodes = sorted({ord(c) for c in characters if ord(c) >= 32})

    search_dir = fonts_dir if fonts_dir and os.path.isdir(fonts_dir) else SCRIPT_DIR

    fonts = []
    for name in FONT_FILES:
        path = os.path.join(search_dir, name)
        if os.path.exists(path):
            fonts.append(TTFont(path))
        else:
            print(f"Warning: file not found: {path}")

    if not fonts:
        print(f"Error: no source font files found in {search_dir}.")
        return 1

    # Map each character to the first font that provides it: char_map[unicode] = (font index, glyph name)
    char_map = {}
    missing_chars = []
    cmaps = [font.getBestCmap() for font in fonts]

    for code in needed_unicodes:
        for i, cmap in enumerate(cmaps):
            if code in cmap:
                char_map[code] = (i, cmap[code])
                break
        else:
            missing_chars.append(code)

    print(f"Characters requested: {len(needed_unicodes)}")
    print(f"Characters found: {len(char_map)}")
    if missing_chars:
        print(f"Characters not in any source font ({len(missing_chars)}): "
              + " ".join(f"U+{c:04X}" for c in missing_chars))

    if not char_map:
        print("Error: none of the requested characters exist in the source fonts.")
        return 1

    # Rebuild the base font's glyph set from scratch, keeping it only as a metadata template.
    base_font = fonts[0]
    new_glyf = {}
    new_hmtx = {}
    new_cmap_dict = {}

    if '.notdef' in base_font['glyf']:
        new_glyf['.notdef'] = base_font['glyf']['.notdef']
        new_hmtx['.notdef'] = base_font['hmtx'].metrics['.notdef']

    # Glyphs from non-base fonts get prefixed names so they can't collide.
    for code, (f_idx, old_name) in char_map.items():
        source_font = fonts[f_idx]

        for d_name in get_dependencies(source_font, old_name):
            new_name = d_name if f_idx == 0 else f"f{f_idx}_{d_name}"
            if new_name in new_glyf:
                continue

            glyph_obj = source_font['glyf'][d_name]
            if glyph_obj.isComposite() and f_idx > 0:
                for comp in glyph_obj.components:
                    comp.glyphName = f"f{f_idx}_{comp.glyphName}"

            new_glyf[new_name] = glyph_obj
            new_hmtx[new_name] = source_font['hmtx'].metrics[d_name]

        new_cmap_dict[code] = old_name if f_idx == 0 else f"f{f_idx}_{old_name}"

    base_font['glyf'].glyphs = new_glyf
    base_font['hmtx'].metrics = new_hmtx

    # .notdef has to stay glyph 0.
    order = sorted(name for name in new_glyf if name != '.notdef')
    if '.notdef' in new_glyf:
        order.insert(0, '.notdef')
    base_font.setGlyphOrder(order)

    for table in base_font['cmap'].tables:
        if table.isUnicode():
            table.cmap = new_cmap_dict

    for tag in DROPPED_TABLES:
        if tag in base_font:
            del base_font[tag]

    print(f"Total glyphs in subset: {len(new_glyf)}")
    os.makedirs(os.path.dirname(os.path.abspath(out_path)), exist_ok=True)
    base_font.save(out_path)
    print(f"Saved {out_path}")

    # The editor bakes the TMP atlas from this list, so it only ever asks for glyphs that exist.
    os.makedirs(os.path.dirname(os.path.abspath(found_path)), exist_ok=True)
    with open(found_path, "w", encoding="utf-8") as f:
        f.write("".join(chr(c) for c in sorted(char_map)))
    print(f"Saved {found_path}")
    return 0


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--chars", default=DEFAULT_CHARS, help="UTF-8 file of characters to include")
    parser.add_argument("--out", default=DEFAULT_OUT, help="merged TTF to write")
    parser.add_argument("--found", default=DEFAULT_FOUND, help="UTF-8 file of characters actually included")
    parser.add_argument("--fonts_dir", default=None, help="Directory containing source Noto fonts")
    args = parser.parse_args()
    sys.exit(merge_and_subset(args.chars, args.out, args.found, args.fonts_dir))
