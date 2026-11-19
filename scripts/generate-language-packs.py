"""Generate machine-translated UI resources; sends only built-in English strings.

Requires maintainer authorization to use Google's web translation endpoint.
Existing translations and a resumable local cache are preserved by default.
"""

import argparse
from concurrent.futures import ThreadPoolExecutor
import json
from pathlib import Path
import re
import time
import urllib.parse
import urllib.request
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
RESOURCES = ROOT / "Libraries/NimCap.Screenshot.Core/Resources"
CACHE = ROOT / ".artifacts/language-cache"
TARGETS = {
    "cs": ["cs"], "de": ["de", "de-AT", "de-CH"],
    "es": ["es", "es-ES", "es-419"], "fr": ["fr", "fr-FR", "fr-CA"],
    "it": ["it"], "ja": ["ja"], "ko": ["ko"], "pl": ["pl"],
    "pt": ["pt-BR"], "pt-PT": ["pt-PT"], "ru": ["ru"], "tr": ["tr"],
    "zh-TW": ["zh-Hant"],
}
PLACEHOLDERS = re.compile(r"(?<!\{)\{\d+(?:,[^}:]+)?(?::[^}]+)?\}(?!\})")
PROTECTED = re.compile(
    r"\{[^{}]+\}|https?://[^\s)]+|\*\.[\w]+|"
    r"\b[\w.-]+\.(?:exe|dll|onnx|json|gguf|traineddata|zip|csv|xlsx|png|jpg|webp)\b|"
    r"\b(?:Ctrl|Alt|Shift|Win)\+[A-Za-z0-9+]+|yyyy-MM-dd|"
    r"\b(?:NimCap Screenshot|NimCap|NimCap.Screenshot|DirectML|ONNX|WGC|DXGI|GDI|WASAPI|FFmpeg|NuGet|"
    r"PaddleOCR|Tesseract|SLANet|LaTeX|BiRefNet|ModelScope|Hugging Face|"
    r"chi_sim\+eng|chi_sim|eng|float32|NCHW|CTC)\b"
)


def traditional_chinese(tree):
    """Convert the established Simplified Chinese pack locally on Windows."""
    import ctypes
    mapper = ctypes.windll.kernel32.LCMapStringEx
    mapper.argtypes = [ctypes.c_wchar_p, ctypes.c_uint, ctypes.c_wchar_p, ctypes.c_int,
                       ctypes.c_wchar_p, ctypes.c_int, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_ssize_t]
    mapper.restype = ctypes.c_int
    glossary = {
        "文件夾": "資料夾", "文件": "檔案", "設置": "設定", "保存": "儲存",
        "界面": "介面", "默認": "預設", "屏幕": "螢幕", "窗口": "視窗",
        "鼠標": "滑鼠", "剪貼板": "剪貼簿", "粘貼": "貼上", "滾動": "捲動",
        "加載": "載入", "內存": "記憶體", "軟件": "軟體", "硬件": "硬體",
        "網絡": "網路", "視頻": "影片", "音頻": "音訊", "設備": "裝置",
        "文本": "文字", "錄屏": "螢幕錄影", "線程": "執行緒",
    }
    for entry in tree.getroot().findall("data"):
        value = entry.findtext("value", "")
        size = mapper("zh-TW", 0x04000000, value, -1, None, 0, None, None, 0)
        if not size:
            raise ctypes.WinError()
        buffer = ctypes.create_unicode_buffer(size)
        if not mapper("zh-TW", 0x04000000, value, -1, buffer, size, None, None, 0):
            raise ctypes.WinError()
        converted = buffer.value
        for source, target in glossary.items():
            converted = converted.replace(source, target)
        entry.find("value").text = converted
    return tree


def write_resources(tree, cultures, overrides):
    for entry in tree.getroot().findall("data"):
        key = entry.attrib["name"]
        if key in overrides:
            entry.find("value").text = overrides[key]
    ET.indent(tree, space="  ")
    for culture in cultures:
        output = RESOURCES / f"Strings.{culture}.resx"
        tree.write(output, encoding="utf-8", xml_declaration=True, short_empty_elements=False)
        print(f"Wrote {output.name}", flush=True)


def protect(value):
    tokens = []

    def replace(match):
        tokens.append(match.group())
        return f"ZXQ{len(tokens) - 1:04d}QXZ"

    return PROTECTED.sub(replace, value), tokens


def restore(value, tokens):
    for index, token in enumerate(tokens):
        pattern = rf"ZXQ\s*{index:04d}\s*QXZ"
        if len(re.findall(pattern, value, re.I)) != 1:
            raise ValueError(f"Lost protected token: {token!r}")
        value = re.sub(pattern, lambda _: token, value, flags=re.I)
    if "ZXQ" in value:
        raise ValueError("Unexpected translation token")
    return value


def request_translation(value, target):
    query = urllib.parse.urlencode({"client": "gtx", "sl": "en", "tl": target, "dt": "t", "q": value})
    request = urllib.request.Request(
        "https://translate.googleapis.com/translate_a/single?" + query,
        headers={"User-Agent": "NimCap-Screenshot localization maintainer"},
    )
    for attempt in range(5):
        try:
            with urllib.request.urlopen(request, timeout=40) as response:
                data = json.load(response)
            return "".join(part[0] for part in data[0] if part[0] is not None)
        except Exception:
            if attempt == 4:
                raise
            time.sleep(2 ** attempt)


def translate_batch(values, target):
    protected = [protect(value) for value in values]
    text = "\n".join(f"\ue000{index:04d}\ue001\n{value}" for index, (value, _) in enumerate(protected))
    result = request_translation(text, target)
    parts = re.split(r"\ue000\s*\d{4}\s*\ue001\s*", result)
    try:
        if len(parts) != len(values) + 1 or parts[0].strip():
            raise ValueError("Translation batch separators changed")
        return [restore(part.strip(), tokens) for part, (_, tokens) in zip(parts[1:], protected, strict=True)]
    except ValueError:
        return [restore(request_translation(value, target).strip(), tokens) for value, tokens in protected]


def translate_language(target, cultures):
    overrides = json.loads((ROOT / "scripts/language-overrides.json").read_text(encoding="utf-8"))[target]
    for name in ("capture-guide-translations.json", "desktop-ruler-translations.json"):
        guides = json.loads((ROOT / "scripts" / name).read_text(encoding="utf-8"))
        language = guides["cultures"][cultures[0]]
        overrides.update(zip(guides["keys"], guides["translations"][language], strict=True))
    if target == "zh-TW":
        tree = traditional_chinese(ET.parse(RESOURCES / "Strings.zh-CN.resx"))
        write_resources(tree, cultures, overrides)
        return
    tree = ET.parse(RESOURCES / "Strings.resx")
    entries = tree.getroot().findall("data")
    CACHE.mkdir(parents=True, exist_ok=True)
    cache_path = CACHE / f"{target}.json"
    cache = json.loads(cache_path.read_text(encoding="utf-8")) if cache_path.exists() else {}
    # File-dialog masks are code. Translate each label separately, preserving masks.
    def phrases(value):
        return value.split("|")[::2] if "|*" in value else [value]
    unique = list(dict.fromkeys(phrase.strip() for entry in entries for phrase in phrases(entry.findtext("value", ""))))
    pending = [value for value in unique if value and value not in cache]
    while pending:
        batch = []
        size = 0
        for value in pending:
            length = len(urllib.parse.quote(protect(value)[0])) + 80
            if batch and size + length > 6500:
                break
            batch.append(value)
            size += length
        translated = translate_batch(batch, target)
        for source, value in zip(batch, translated, strict=True):
            if not value or sorted(PLACEHOLDERS.findall(source)) != sorted(PLACEHOLDERS.findall(value)):
                raise ValueError(f"{target}: invalid translation: {source!r} -> {value!r}")
            cache[source] = value
        cache_path.write_text(json.dumps(cache, ensure_ascii=False, indent=2), encoding="utf-8")
        pending = pending[len(batch):]
        print(f"{target}: {len(unique) - len(pending)}/{len(unique)}", flush=True)

    def lookup(value):
        leading = value[:len(value) - len(value.lstrip())]
        trailing = value[len(value.rstrip()):]
        return leading + cache.get(value.strip(), value.strip()) + trailing

    for entry in entries:
        source = entry.findtext("value", "")
        if "|*" in source:
            parts = source.split("|")
            parts[::2] = [lookup(part) for part in parts[::2]]
            value = "|".join(parts)
        else:
            value = lookup(source)
        entry.find("value").text = value
    write_resources(tree, cultures, overrides)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("targets", nargs="*")
    parser.add_argument("--overwrite", action="store_true", help="Replace existing resource files (including manual edits)")
    args = parser.parse_args()
    targets = args.targets or list(TARGETS)
    if set(targets) - TARGETS.keys():
        parser.error("Unknown translation target")
    tasks = []
    for target in targets:
        cultures = [culture for culture in TARGETS[target]
                    if args.overwrite or not (RESOURCES / f"Strings.{culture}.resx").exists()]
        if cultures:
            tasks.append((target, cultures))
    with ThreadPoolExecutor(max_workers=3) as executor:
        futures = [executor.submit(translate_language, target, cultures) for target, cultures in tasks]
        for future in futures:
            future.result()


if __name__ == "__main__":
    main()
