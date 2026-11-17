"""Validate shared UI resources without building or launching SnapSharp."""
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent.parent
RESOURCES = ROOT / "Libraries/SnapSharp.Core/Resources"


def load(path):
    entries = ET.parse(path).getroot().findall("data")
    result = {entry.attrib["name"]: entry.findtext("value", "") for entry in entries}
    if len(result) != len(entries):
        raise ValueError(f"Duplicate resource keys in {path}")
    return result


def placeholders(value):
    return sorted(re.findall(r"(?<!\{)\{\d+(?:,[^}:]+)?(?::[^}]+)?\}(?!\})", value))


def main():
    english = load(RESOURCES / "Strings.resx")
    errors = []
    for path in RESOURCES.glob("Strings.*.resx"):
        translated = load(path)
        if english.keys() != translated.keys():
            errors.append(f"{path.name}: missing {english.keys() - translated.keys()}, extra {translated.keys() - english.keys()}")
        for key in english.keys() & translated.keys():
            if placeholders(english[key]) != placeholders(translated[key]):
                errors.append(f"{path.name}: placeholder mismatch for {key}")
    for key, value in english.items():
        if re.search(r"[\u4e00-\u9fff]", value):
            errors.append(f"Untranslated English: {key}")
    for directory in (ROOT, ROOT / "Views", ROOT / "Services", ROOT / "Libraries"):
        paths = directory.glob("*") if directory == ROOT else directory.rglob("*")
        for path in paths:
            if path.suffix not in (".cs", ".xaml") or any(part in ("bin", "obj") for part in path.parts):
                continue
            text = path.read_text(encoding="utf-8-sig")
            references = re.findall(r'L\.(?:Get|Format)\("([^"\n]+)"', text)
            references += re.findall(r"loc:Loc SnapSharp.Core:Resources.Strings:([A-Za-z0-9_]+)", text)
            for key in references:
                if key not in english:
                    errors.append(f"{path.relative_to(ROOT)}: missing key {key}")
    if errors:
        print("\n".join(errors))
        return 1
    print(f"PASS: {len(english)} resource keys; translation keys, placeholders and source references match.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
