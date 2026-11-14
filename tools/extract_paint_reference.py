"""Read the user's PSD layers into embedded numerical material data; never edit the PSDs."""
import argparse
import hashlib
import json
import struct
from pathlib import Path

from psd_tools import PSDImage

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("reference", type=Path, help="Light-stain reference PSD")
parser.add_argument("wear_reference", type=Path, help="Transparent-wear reference PSD")
args = parser.parse_args()
reference = args.reference
wear_reference = args.wear_reference
psd = PSDImage.open(reference)
layers = list(psd.descendants())
wear_layers = list(PSDImage.open(wear_reference).descendants())
planes = []

for index, is_mask in [(3, True), (4, False), (5, False), (7, True), (8, True), (10, True), (11, True), (12, True)]:
    layer = layers[index]
    image = layer.mask.topil() if is_mask else layer.topil().getchannel("R")
    planes.append((layer.name, image.crop((14, 0, 174, image.height))))
for label in ["轻微透明磨损", "01 灰褐色脏斑", "02 漆面零散露底"]:
    layer = next(layer for layer in wear_layers if layer.name.startswith(label))
    image = layer.mask.topil()
    planes.append((layer.name, image.crop((14, 0, 174, image.height))))

destination = ROOT / "assets" / "paint"
destination.mkdir(exist_ok=True)
with (destination / "reference.bin").open("wb") as stream:
    stream.write(b"PSDPAINT1")
    stream.write(struct.pack("<I", len(planes)))
    for _, image in planes:
        stream.write(struct.pack("<II", image.width, image.height))
        stream.write(image.tobytes())
metadata = {
    "sources": [{"path": path.name, "sha256": hashlib.sha256(path.read_bytes()).hexdigest()} for path in [reference, wear_reference]],
    "crop": "Read-only interior strip x=14..173 within each 188-pixel PSD layer; excludes the source marking's outer silhouette.",
    "planes": [{"name": name, "width": image.width, "height": image.height} for name, image in planes],
    "reference_color": "#FFAB09",
    "blend_modes": ["normal 204/255", "soft light 66/255", "overlay 36/255", "screen 31/255", "multiply 23/255", "multiply 31/255", "multiply 41/255", "multiply 31/255"],
}
(destination / "SOURCE.json").write_text(json.dumps(metadata, ensure_ascii=False, indent=2), encoding="utf-8")
print("Extracted", len(planes), "PSD planes into", destination / "reference.bin")
