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
    image = Image.new("RGB", (1200, 630), "#172b47")
    draw = ImageDraw.Draw(image)
    for x in range(760, 1200, 30):
        for y in range(30, 630, 30):
            draw.ellipse((x, y, x + 2, y + 2), fill="#2c4263")
    logo = logo.resize((42, 42), Image.Resampling.LANCZOS)
    image.paste(logo, (55, 54), logo)
    draw.text((109, 53), "SnapSharp", font=font(30, True), fill="#f4f7ff")
    chinese = language == "zh"
    draw.text((56, 155), "截图之后，" if chinese else "Screenshots.", font=font(49, True, chinese), fill="#f4f7ff")
    draw.text((56, 227), "还可以做更多。" if chinese else "And beyond.", font=font(49, True, chinese), fill="#85abff")
    draw.rounded_rectangle((56, 326, 108, 331), radius=2, fill="#6193ff")
    draw.text((56, 365), "截图标注 · 桌面贴图 · 长截图" if chinese else "Capture. Annotate. Pin.", font=font(21, chinese=chinese), fill="#c1cfe5")
    draw.text((56, 406), "本地 OCR · 表格 · 公式 · 抠图 · 翻译" if chinese else "Local OCR, tables, formulas & more.", font=font(18, chinese=chinese), fill="#c1cfe5")
    draw.text((56, 550), "WINDOWS 10 / 11   ·   OPEN SOURCE", font=font(14, True), fill="#9db1d0")
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
    (IMAGES / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
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
