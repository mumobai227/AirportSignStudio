"""Compare independent SVG rendering and assemble the delivery preview."""
from pathlib import Path
import json
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter

root = Path(__file__).resolve().parents[1]
qa = root / "validation_v3_1"
comparisons = []
for name in ["v3_1_merged_atlas"]:
    a = np.asarray(Image.open(qa / f"{name}.png").convert("RGBA"))
    b = np.asarray(Image.open(qa / f"{name}-svg-browser.png").convert("RGBA"))
    assert a.shape == b.shape
    silhouette_a, silhouette_b = a[:, :, 3] > 127, b[:, :, 3] > 127
    # Compare yellow ink for transparent designs, dark ink for filled boards.
    if name.endswith('_transparent'):
        ink_a = silhouette_a & (a[:, :, 0] > 200)
        ink_b = silhouette_b & (b[:, :, 0] > 200)
        coverage_a, coverage_b = ink_a, ink_b
    else:
        ink_a = silhouette_a & (a[:, :, :3].max(2) < 70)
        ink_b = silhouette_b & (b[:, :, :3].max(2) < 70)
        coverage_a = silhouette_a & (a[:, :, :3].max(2) < 230)
        coverage_b = silhouette_b & (b[:, :, :3].max(2) < 230)
    iou = lambda x, y: float((x & y).sum() / max(1, (x | y).sum()))
    dilate = lambda mask: np.asarray(Image.fromarray((mask * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(3))) > 0
    # GDI+ and Chromium use different pixel coverage at edges.
    # A narrow tip can be fully covered in one rasterizer and partially covered
    # in the other. Match solid ink against the other's edge coverage, within 1 px.
    solid_distant = (ink_a & ~dilate(ink_b)) | (ink_b & ~dilate(ink_a))
    distant = (ink_a & ~dilate(coverage_b)) | (ink_b & ~dilate(coverage_a))
    alpha_distant = (silhouette_a & ~dilate(silhouette_b)) | (silhouette_b & ~dilate(silhouette_a))
    report = dict(sample=name, silhouette_iou=iou(silhouette_a, silhouette_b), ink_iou=iou(ink_a, ink_b), solid_threshold_mismatch_beyond_one_pixel=int(solid_distant.sum()), ink_missing_edge_coverage_beyond_one_pixel=int(distant.sum()), alpha_mismatch_beyond_one_pixel=int(alpha_distant.sum()))
    # Thin transparent outlines have a much higher boundary/area ratio than boards.
    # Apply the same one-pixel coverage tolerance to alpha and to coloured ink.
    assert report["alpha_mismatch_beyond_one_pixel"] == 0 and report["ink_missing_edge_coverage_beyond_one_pixel"] == 0 and report["ink_iou"] > 0.99, report
    comparisons.append(report)
(qa / "svg-comparison.json").write_text(json.dumps(comparisons, indent=2), encoding="utf-8")
print(json.dumps(comparisons, indent=2))

font = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 24)
small = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 16)
sheet = Image.new("RGB", (1240, 1360), (237, 242, 242))
draw = ImageDraw.Draw(sheet)
draw.text((28, 17), "机场标志工坊 3.1  /  合并区域与自由边界", font=font, fill=(30, 49, 59))
items = [
    ("合并区域与局部调整", "v3_1_merged_atlas", "12 格合并为 5 区 · 上下行使用不同列宽"),
    ("组合牌自由尺寸", "v2_custom_taxi", "分段宽度 1200 / 1800 mm · 整体高度 700 mm"),
    ("MAX SPAN", "v2_ground_MAX_SPAN", "两行翼展信息 · 小写米单位"),
    ("HP 位置识别点", "v2_ground_HP", "黑底黄字 · 自动加 HP 前缀"),
    ("NO ENTRY", "v2_ground_NO_ENTRY", "红底白字 · 禁止进入"),
    ("机型文字", "v2_ground_aircraft_transparent", "透明底黄色文字 · A320/B737"),
]
for i, (title, filename, caption) in enumerate(items):
    x, y = 20 + (i % 2) * 610, 64 + (i // 2) * 432
    draw.rounded_rectangle((x, y, x + 600, y + 418), radius=9, fill=(35, 45, 51))
    draw.text((x + 22, y + 14), f"0{i+1}  {title}", font=font, fill=(232, 241, 241))
    draw.text((x + 22, y + 382), caption, font=small, fill=(174, 194, 202))
    im = Image.open(qa / f"{filename}.png").convert("RGBA")
    im = im.crop(im.getchannel("A").getbbox())
    im.thumbnail((552, 306), Image.Resampling.LANCZOS)
    sheet.paste(im, (x + (600 - im.width) // 2, y + 64 + (306 - im.height) // 2), im)
sheet.save(root / "release_v3_1" / "新版功能示例.png")
