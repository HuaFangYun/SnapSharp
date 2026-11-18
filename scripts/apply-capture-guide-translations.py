"""Apply reviewed capture-guide translations locally, preserving resource formatting."""
import json
from pathlib import Path
import re
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[1]


def main():
    catalog = json.loads((ROOT / "scripts/capture-guide-translations.json").read_text(encoding="utf-8"))
    for culture, language in catalog["cultures"].items():
        suffix = "" if culture == "en" else "." + culture
        path = ROOT / f"Libraries/SnapSharp.Core/Resources/Strings{suffix}.resx"
        content = path.read_text(encoding="utf-8-sig")
        for key, value in zip(catalog["keys"], catalog["translations"][language], strict=True):
            pattern = r'(<data name="' + re.escape(key) + r'"[^>]*>\s*<value>).*?(</value>\s*</data>)'
            content, count = re.subn(pattern, lambda match: match[1] + escape(value) + match[2], content, flags=re.S)
            if not count:
                entry = f'  <data name="{key}" xml:space="preserve"><value>{escape(value)}</value></data>\n'
                content = content.replace("</root>", entry + "</root>")
        path.write_text(content, encoding="utf-8")
    print(f'Applied {len(catalog["keys"])} reviewed translations to {len(catalog["cultures"])} resource packs.')


if __name__ == "__main__":
    main()
