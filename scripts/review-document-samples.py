"""Compare a document-image diagnostic run against a manually inspected subset.

Checks the explicitly listed document types and field values.
Does not make network requests or change images.
"""
import argparse
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("run", type=Path)
    parser.add_argument("--reference", type=Path, default=ROOT / "tools/WindowChecks/document-sample-reference.json")
    args = parser.parse_args()
    reference = json.loads(args.reference.read_text(encoding="utf-8"))["samples"]
    found = set()
    checks = []
    for file in sorted(args.run.glob("photo-*-blocks.json")):
        result = json.loads(file.read_text(encoding="utf-8"))
        stem = Path(result["input"]).stem
        if stem not in reference:
            continue
        found.add(stem)
        expected = reference[stem]
        checks.append({"sample": stem, "field": "kind", "expected": expected["kind"], "actual": result["kind"], "pass": expected["kind"] == result["kind"]})
        fields = {field["Name"]: field for field in result["fields"]}
        for name, value in expected["fields"].items():
            actual = fields.get(name, {}).get("Value")
            checks.append({"sample": stem, "field": name, "expected": value, "actual": actual, "pass": value == actual})
    missing = sorted(set(reference) - found)
    failures = [check for check in checks if not check["pass"]]
    report = {"reference": str(args.reference), "samples": len(found), "checks": len(checks), "passed": len(checks) - len(failures),
              "missing_samples": missing, "differences": failures, "scope": "Only explicitly listed document types and fields are compared."}
    (args.run / "reference-comparison.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False, indent=2))
    if missing:
        raise SystemExit(1)


if __name__ == "__main__":
    main()
