"""Trace the supplied 2021 PDF diagrams. Build-time only; never needed by the EXE."""
import argparse
import hashlib
import json
from pathlib import Path
import sys
import xml.etree.ElementTree as ET

parser = argparse.ArgumentParser()
parser.add_argument("pdf", type=Path)
parser.add_argument("--deps", action="append", default=[])
args = parser.parse_args()
for path in args.deps:
    sys.path.insert(0, path)

import numpy as np
from PIL import Image, ImageDraw
from pypdf import PdfReader
from scipy import ndimage
import potrace

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "assets"
QA = ROOT / "validation"
OUT.mkdir(exist_ok=True)
QA.mkdir(exist_ok=True)
pdf = PdfReader(args.pdf)
root = ET.Element("glyphs", source="MH 5001-2021", height="1000")
records, previews = [], []


def extract(page, family, chars, row_height):
    image = pdf.pages[page - 1].images[0].image.convert("RGB")
    rgb = np.asarray(image).astype(np.int16)
    mask = (rgb[:, :, 1] - rgb[:, :, 0] > 35) & (rgb[:, :, 1] - rgb[:, :, 2] > 20)
    mask = ndimage.binary_closing(mask, structure=np.ones((7, 7)))
    labels, _ = ndimage.label(mask)
    slices = ndimage.find_objects(labels)
    components = []
    for label, sl in enumerate(slices, 1):
        if sl is None:
            continue
        part = labels[sl] == label
        if part.sum() > 450:
            components.append((sl, part))
    components.sort(key=lambda v: (int(((v[0][0].start + v[0][0].stop) / 2) // row_height), v[0][1].start))
    assert len(components) == len(chars), (page, len(components), len(chars))
    for char, (sl, part) in zip(chars, components):
        # Remove only tiny grid remnants, preserving letter counters.
        holes, _ = ndimage.label(~part)
        sizes = np.bincount(holes.ravel())
        small = sizes < 35
        small[0] = False
        part |= small[holes]
        # Grid intersections at the outline leave 1-3 px notches. Smooth that
        # raster noise before fitting curves; retain the original ink bounds.
        part = ndimage.gaussian_filter(np.pad(part.astype(float), 5), 2.3) >= 0.48
        part = part[5:-5, 5:-5]
        h, w = part.shape
        normalized_height = 1000
        if char == ".":
            normalized_height = 160
        if char == "-":
            normalized_height = 120
        scale = normalized_height / h
        padded = np.pad(part, 8)
        bitmap = Image.fromarray(np.where(padded, 0, 255).astype(np.uint8))
        curves = potrace.Bitmap(bitmap).trace(turdsize=2, alphamax=1.3, opticurve=True, opttolerance=1.5)
        commands = []

        def point(p):
            return f"{(p.x - 8) * scale:.3f} {(p.y - 8) * scale:.3f}"

        for curve in curves:
            commands.append("M " + point(curve.start_point))
            for segment in curve:
                if segment.is_corner:
                    commands.extend(["L " + point(segment.c), "L " + point(segment.end_point)])
                else:
                    commands.append("C " + point(segment.c1) + " " + point(segment.c2) + " " + point(segment.end_point))
            commands.append("Z")
        d = " ".join(commands)
        ET.SubElement(root, "glyph", family=family, char=char, width=f"{w * scale:.3f}", height=str(normalized_height), page=str(page), d=d)
        holes_count = ndimage.label(ndimage.binary_fill_holes(np.pad(part, 1)) & ~np.pad(part, 1))[1]
        records.append(dict(family=family, char=char, pdf_page=page, source_box=[sl[1].start, sl[0].start, w, h], counters=holes_count))
        tile = Image.new("RGB", (120, 190), "#f3f4f5")
        ink = Image.fromarray(np.where(part, 25, 245).astype(np.uint8)).convert("RGB")
        ink.thumbnail((96, 145), Image.Resampling.LANCZOS)
        tile.paste(ink, ((120 - ink.width) // 2, 10))
        ImageDraw.Draw(tile).text((8, 166), f"{family}: {char if char.isascii() else 'arrow'}", fill="black")
        previews.append(tile)


for page, chars in [(86, "ABCDEFGH"), (87, "IJKLMNOP"), (88, "QRSTUVWX"), (89, "YZ123456"), (90, "7890↑")]:
    extract(page, "ground", chars, 610)
for page, chars in [(262, "ABCDEFGHIJKLMNOP"), (263, "QRSTUVWXYZ123456"), (264, "7890ⅠⅡⅢ/.↑-")]:
    extract(page, "sign", chars, 300)

# The ground diagram has no punctuation samples. These simple supplemental
# shapes are explicitly identified, rather than attributed to the diagram.
ET.SubElement(root, "glyph", family="ground", char="-", width="300", height="100", page="supplement", d="M 0 0 L 300 0 L 300 100 L 0 100 Z")
ET.indent(root)
ET.ElementTree(root).write(OUT / "glyphs.xml", encoding="utf-8", xml_declaration=True)
sheet = Image.new("RGB", (120 * 10, 190 * ((len(previews) + 9) // 10)), "white")
for i, tile in enumerate(previews):
    sheet.paste(tile, ((i % 10) * 120, (i // 10) * 190))
sheet.save(QA / "glyph-contact-sheet.png")
report = dict(source=str(args.pdf), sha256=hashlib.sha256(args.pdf.read_bytes()).hexdigest(),
              method="Colour-separated raster diagrams, 7px grid closing, 2.3px outline smoothing, Potrace outlines; sign widths set separately by Table H.0.9.", glyphs=records)
(QA / "glyph-provenance.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
print(f"Extracted {len(records)} glyphs from 2021 diagrams.")
