"""Rebuild the server-only fallback fonts with fonttools==4.61.1.

The upstream CJK variable font defaults to Thin. Pinning Bold also removes
unused variation tables; the emoji outlines use their regular weight.
Copyright and OFL metadata remain in the generated fonts and adjacent licenses.
"""

import argparse
import hashlib
import io
from pathlib import Path
from urllib.request import urlopen

import fontTools
from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont


REVISION = "47d54ca1ebb14fd864695f1d39dc734f39017c33"
BASE = f"https://raw.githubusercontent.com/google/fonts/{REVISION}/ofl"
FONTS = (
    ("NotoSansSC.ttf", "notosanssc/NotoSansSC%5Bwght%5D.ttf", 700,
     "a3041811a78c361b1de50f953c805e0244951c21c5bd412f7232ef0d899af0da"),
    ("NotoEmoji.ttf", "notoemoji/NotoEmoji%5Bwght%5D.ttf", 400,
     "de6c18832938afc99caf132b39d6a30a19bac7f2e812e28db2535b4608d27551"),
)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path, help="Optional directory of original, unmodified font files")
    parser.add_argument("--output", type=Path,
                        default=Path(__file__).resolve().parents[1] / "wwwroot/assets/fonts/share-cards")
    args = parser.parse_args()
    if fontTools.__version__ != "4.61.1":
        raise RuntimeError("Reproducible output requires fonttools==4.61.1")
    args.output.mkdir(parents=True, exist_ok=True)
    for filename, source, weight, digest in FONTS:
        if args.input:
            original = (args.input / filename).read_bytes()
        else:
            with urlopen(f"{BASE}/{source}", timeout=30) as response:
                original = response.read()
        if hashlib.sha256(original).hexdigest() != digest:
            raise ValueError(f"Unexpected upstream font: {filename}")
        font = TTFont(io.BytesIO(original), recalcTimestamp=False)
        instantiateVariableFont(font, {"wght": weight}, inplace=True, updateFontNames=True)
        if "fvar" in font or font["OS/2"].usWeightClass != weight:
            raise ValueError(f"Failed to pin font weight: {filename}")
        output = args.output / filename
        font.save(output)
        print(f"{filename}: {output.stat().st_size} bytes, weight {weight}")


if __name__ == "__main__":
    main()
