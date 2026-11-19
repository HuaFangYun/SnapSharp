#!/usr/bin/env python3
"""Prepare website images from existing product captures; requires Pillow.

This is an editorial asset step, not a deployment dependency. Generated images
are committed so GitHub Pages builds need only the Python standard library.
"""

import json
import shutil
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont, ImageOps

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "website/assets"
IMAGES = ASSETS / "images"
SOURCES = {
    "annotation": "01-annotation-editor.png",
    "ocr": "14-ai-ocr-chinese.png",
    "table": "16-ai-table-recognition.png",
    "formula": "17-ai-formula-latex.png",
    "cutout": "19-ai-cutout-comparison.png",
    "pins": "12-pin-workspace.png",
    "translation": "20-ai-local-translation.png",
}


def font(size, bold=False, chinese=False):
    fonts = Path("C:/Windows/Fonts")
    names = (["msyhbd.ttc", "msyh.ttc"] if chinese else ["segoeuib.ttf" if bold else "segoeui.ttf"]) + ["DejaVuSans-Bold.ttf" if bold else "DejaVuSans.ttf"]
    for name in names:
        try:
            return ImageFont.truetype(str(fonts / name) if (fonts / name).exists() else name, size)
        except OSError:
            continue
    raise RuntimeError("Install Microsoft YaHei / Segoe UI or supply equivalent fonts before regenerating social cards.")


def social_card(language, logo):
    image = Image.new("RGB", (1200, 630), "#111d1c")
    draw = ImageDraw.Draw(image)
    for x in range(760, 1200, 30):
        for y in range(30, 630, 30):
            draw.ellipse((x, y, x + 2, y + 2), fill="#30443e")
    logo = logo.resize((42, 42), Image.Resampling.LANCZOS)
    image.paste(logo, (55, 54), logo)
    draw.text((109, 53), "NimCap Screenshot", font=font(30, True), fill="#f4f7ff")
    chinese = language == "zh"
    draw.text((56, 155), "截图之后，" if chinese else "Capture it.", font=font(49, True, chinese), fill="#f4f7ff")
    draw.text((56, 227), "交给本地 AI。" if chinese else "Keep it local.", font=font(49, True, chinese), fill="#d8f58a")
    draw.rounded_rectangle((56, 326, 108, 331), radius=2, fill="#d8f58a")
    draw.text((56, 365), "本地 OCR · 表格 · 公式" if chinese else "Local OCR. Tables. Formulas.", font=font(21, chinese=chinese), fill="#c8d8d1")
    draw.text((56, 406), "Qwen 翻译 · ONNX 抠图" if chinese else "Qwen translation. ONNX cutouts.", font=font(21, chinese=chinese), fill="#c8d8d1")
    draw.text((56, 550), "WINDOWS   /   OPEN SOURCE   /   LOCAL AI", font=font(14, True), fill="#9aae9f")
    shot = Image.open(ROOT / "Screenshot/Blog/01-annotation-editor.png").convert("RGB")
    shot = ImageOps.contain(shot, (586, 440), Image.Resampling.LANCZOS)
    x, y = 577, 151
    draw.rounded_rectangle((x - 8, y - 31, x + shot.width + 8, y + shot.height + 8), radius=12, fill="#24282f", outline="#45516a", width=1)
    for n in range(3):
        draw.ellipse((x + 6 + n * 13, y - 19, x + 12 + n * 13, y - 13), fill="#74839b")
    image.paste(shot, (x, y))
    image.save(IMAGES / f"social-{language}.png", optimize=True)


def main():
    IMAGES.mkdir(parents=True, exist_ok=True)
    manifest = {}
    for name, filename in SOURCES.items():
        source = Image.open(ROOT / "Screenshot/Blog" / filename).convert("RGB")
        widths = sorted(set(min(width, source.width) for width in [640, 1280]))
        for width in widths:
            result = source.resize((width, round(source.height * width / source.width)), Image.Resampling.LANCZOS)
            result.save(IMAGES / f"{name}-{width}.webp", quality=90, method=6)
        source.save(IMAGES / f"{name}-full.webp", lossless=True, method=6)
        manifest[name] = {"width": source.width, "height": source.height, "widths": widths, "source": f"Screenshot/Blog/{filename}"}
    # Aligned crops of the existing actual before/after inference comparison.
    comparison = Image.open(ROOT / 'Screenshot/Blog/19-ai-cutout-comparison.png').convert('RGB')
    for name, bounds in [('cutout-before', (67, 229, 717, 1150)), ('cutout-after', (783, 229, 1433, 1150))]:
        crop = comparison.crop(bounds)
        for width in [640]:
            crop.resize((width, round(crop.height * width / crop.width)), Image.Resampling.LANCZOS).save(IMAGES / f'{name}-{width}.webp', quality=92, method=6)
        crop.save(IMAGES / f'{name}-full.webp', lossless=True, method=6)
        manifest[name] = {'width': crop.width, 'height': crop.height, 'widths': [640], 'source': 'Screenshot/Blog/19-ai-cutout-comparison.png', 'crop': bounds}
    (IMAGES / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline='\n')
    shutil.copyfile(ROOT / "logo.ico", ASSETS / "favicon.ico")
    logo = Image.open(ROOT / "logo.ico").convert("RGBA")
    logo.resize((64, 64), Image.Resampling.LANCZOS).save(ASSETS / "logo.png")
    background = Image.new("RGBA", (180, 180), "#398bff")
    background.alpha_composite(logo.resize((180, 180), Image.Resampling.LANCZOS))
    background.convert("RGB").save(ASSETS / "apple-touch-icon.png")
    for language in ["zh", "en"]:
        social_card(language, logo)
    total = sum(p.stat().st_size for p in IMAGES.iterdir())
    print(f"Prepared {len(SOURCES)} screenshot sets and 2 social cards ({total / 1024:.0f} KiB).")


if __name__ == "__main__":
    main()
