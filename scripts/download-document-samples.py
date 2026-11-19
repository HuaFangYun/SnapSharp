"""Download public document OCR demos for the optional local image audit.

The manifest records source URLs and hashes for the demonstration images.
Images stay in the ignored .artifacts
directory and are never submitted to an OCR service by this script.
"""
import argparse
import concurrent.futures
import hashlib
import json
from pathlib import Path
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
CATEGORIES = {
    "id-front": ("card", "IDCardOCR", 3),
    "id-back": ("card", "IDCardBackOCR", 3),
    "driving": ("vehicle", "DriverLicenseOCR", 3),
    "vehicle": ("vehicle", "VehicleLicenseOCR", 3),
    "household": ("card", "ResidenceBookletOCR", 2),
    "passport": ("card", "PassportOCR", 2),
    "bank": ("card", "BankCardOCR", 3),
    "business": ("card", "BizLicenseOCR", 3),
}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=ROOT / ".artifacts/document-web-audit")
    args = parser.parse_args()
    out = args.output.resolve()
    image_dir = out / "images"
    image_dir.mkdir(parents=True, exist_ok=True)
    entries = []
    for category, (group, api, count) in CATEGORIES.items():
        for index in range(1, count + 1):
            entries.append({"name": f"{category}-{index:02}", "category": category,
                            "source": "https://cloud.tencent.com/product/ocr",
                            "url": f"https://ocr-demo-1254418846.cos.ap-guangzhou.myqcloud.com/{group}/{api}/{api}{index}.jpg"})
    for index, extension in enumerate(("jpeg", "png"), 1):
        entries.append({"name": f"id-github-{index:02}", "category": "id-supplemental",
                        "source": "https://github.com/chineseocr/chineseocr/tree/master/test",
                        "url": f"https://raw.githubusercontent.com/chineseocr/chineseocr/master/test/idcard-demo.{extension}"})

    def fetch(entry):
        result = dict(entry)
        try:
            request = urllib.request.Request(entry["url"], headers={"User-Agent": "Mozilla/5.0"})
            with urllib.request.urlopen(request, timeout=30) as response:
                data = response.read(20_000_001)
            if len(data) > 20_000_000 or not (data.startswith(b"\xff\xd8") or data.startswith(b"\x89PNG\r\n\x1a\n")):
                raise ValueError("Expected a JPEG/PNG of at most 20 MB")
            destination = image_dir / (entry["name"] + (".png" if data.startswith(b"\x89PNG") else ".jpg"))
            destination.write_bytes(data)
            result.update(file=str(destination), sha256=hashlib.sha256(data).hexdigest(), bytes=len(data))
        except Exception as error:
            result["error"] = str(error)
        return result

    results = list(concurrent.futures.ThreadPoolExecutor(6).map(fetch, entries))
    (out / "sources.json").write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding="utf-8")
    for result in results:
        print(result["name"], result.get("error", result.get("file")))
    if any("error" in result for result in results):
        raise SystemExit(1)


if __name__ == "__main__":
    main()
