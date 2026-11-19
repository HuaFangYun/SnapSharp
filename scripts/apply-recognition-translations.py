"""Apply reviewed document-recognition strings; other languages use English for new keys."""
import json
from pathlib import Path
import re
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[1]
translations = json.loads((ROOT / "scripts/recognition-translations.json").read_text(encoding="utf-8"))
for path in (ROOT / "Libraries/SnapSharp.Core/Resources").glob("Strings*.resx"):
    culture = path.name.removeprefix("Strings.").removesuffix(".resx")
    source = "zh-CN" if culture in ("zh-CN", "zh-Hans") else "zh-Hant" if culture == "zh-Hant" else "en"
    text = path.read_text(encoding="utf-8")
    for key, value in translations[source].items():
        pattern = r'(<data name="' + re.escape(key) + r'"[^>]*><value>).*?(</value></data>)'
        text, count = re.subn(pattern, lambda match: match[1] + escape(value) + match[2], text, flags=re.S)
        if count == 0:
            text = text.replace("</root>", f'  <data name="{key}" xml:space="preserve"><value>{escape(value)}</value></data>\n</root>')
        elif count != 1:
            raise ValueError(f"Expected one {key} in {path.name}, found {count}")
    path.write_text(text, encoding="utf-8")
