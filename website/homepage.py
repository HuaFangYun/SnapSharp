"""Render the bilingual homepage from HTML; no browser JavaScript is required."""

import re
from html import escape
from pathlib import Path
from string import Template


def render(site):
    source = Path(__file__).with_name("templates") / "home.html"
    markup = source.read_text(encoding="utf-8")
    # Editorial pairs live beside their semantic HTML, Chinese first.
    markup = re.sub(r"\{\{(.*?)\|\|(.*?)\}\}",
                    lambda match: site.t((match[1], match[2])), markup,
                    flags=re.DOTALL)
    values = {
        "download": "https://github.com/nimcap/nimcap-screenshot/releases/latest",
        "repository": "https://github.com/nimcap/nimcap-screenshot",
        "setup": site.href("start/"),
        "guides": site.guides(),
        "faq": site.faq(),
        "provenance": "https://github.com/nimcap/nimcap-screenshot/blob/main/Screenshot/Blog/README.md",
    }
    images = {
        "watermark": ("本地 LaMa 去水印：左侧原图、右侧处理结果", "Local LaMa watermark removal: original on the left, result on the right"),
        "ocr": ("本地 PaddleOCR 的文字识别界面与结果", "Local PaddleOCR interface and text recognition result"),
        "table": ("本地表格识别与可编辑单元格", "Local table recognition and editable cells"),
        "formula": ("图片公式识别为可编辑 LaTeX", "A formula image recognized as editable LaTeX"),
        "cutout": ("原图与本地 BiRefNet General Lite 抠图结果对比", "Original image and actual local BiRefNet General Lite cutout"),
        "translation": ("已有本地 Qwen 翻译的结果局部", "Detail from an existing local Qwen translation result"),
        "annotation": ("截图编辑器中的箭头、文字与序号标注", "Arrows, text, and numbered annotations in the screenshot editor"),
    }
    for name, alt in images.items():
        image = site.image(name, alt, eager=name == "watermark",
                           sizes="(max-width: 760px) 92vw, (max-width: 1100px) 60vw, 760px" if name == "watermark" else "(max-width: 760px) 92vw, 600px")
        values["image_" + name] = site.link(
            site.url(f"assets/images/{name}-full.webp"), image,
            css="screenshot-link",
            extra=f'data-zoom data-caption="{escape(site.t(alt), quote=True)}"')
    for name in ("capture", "shield", "download", "text", "table", "formula", "cutout", "pin", "monitor", "video", "image", "check", "github"):
        values["icon_" + name] = site.home_icon(name)
    return Template(markup).substitute(values)
