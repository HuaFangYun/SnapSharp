"""Build the embedded native SLBR installer recipe from verified local fixtures.

Development only. Runtime copies pinned byte ranges, never executes Python/pickle.
Each source recipe reconstructs exactly the same SHA256-pinned ONNX, including the
official batch-normalization statistics. The community checkpoint's changed BN
buffers are not used. No checkpoints or complete weight files are embedded.
"""
import argparse
import base64
import gzip
import hashlib
import json
from pathlib import Path


def fields(data, start=0, end=None):
    end = len(data) if end is None else end
    def varint(pos):
        result = shift = 0
        while True:
            value = data[pos]
            pos += 1
            result |= (value & 127) << shift
            if value < 128:
                return result, pos
            shift += 7
    while start < end:
        tag, start = varint(start)
        wire = tag & 7
        if wire == 0:
            _, start = varint(start)
        elif wire in (1, 5):
            start += 8 if wire == 1 else 4
        elif wire == 2:
            length, start = varint(start)
            yield tag >> 3, start, start + length
            start += length
        else:
            raise ValueError("Unsupported protobuf wire type")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--onnx", type=Path, required=True)
    parser.add_argument("--official", type=Path, required=True)
    parser.add_argument("--domestic", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    data = args.onnx.read_bytes()
    ranges = []
    for field, a, b in fields(data):
        if field == 7:  # ModelProto.graph
            for child, c, d in fields(data, a, b):
                if child == 5:  # GraphProto.initializer
                    for member, e, f in fields(data, c, d):
                        if member == 9 and f - e >= 128:  # TensorProto.raw_data
                            ranges.append((e, f))
    result = {"OutputSha256": hashlib.sha256(data).hexdigest(), "OutputSize": len(data), "Sources": {}}
    expected = {"Official": "be5c0a89513803c8ce56b1768e0c6296900f6ef76df8cbda12900324f9b1caf9",
                "Domestic": "f3984bd73e8eff5bfd69ad4786788c049a934fc8619821e4d9b9605c31a5d9b0"}
    for kind, path in (("Official", args.official), ("Domestic", args.domestic)):
        raw = path.read_bytes()
        digest = hashlib.sha256(raw).hexdigest()
        if digest != expected[kind]:
            raise ValueError("Unrecognized checkpoint: " + kind)
        segments = []
        reconstructed = bytearray()
        position = copied = 0
        for a, b in ranges:
            offset = raw.find(data[a:b])
            if offset < 0:
                # Only small official BN statistics may be retained in graph literals.
                if b - a > 4096:
                    raise ValueError(f"Trainable tensor missing from {kind}: {b-a} bytes")
                continue
            if a > position:
                literal = data[position:a]
                segments.append({"Data": base64.b64encode(literal).decode("ascii")})
                reconstructed.extend(literal)
            segments.append({"Offset": offset, "Length": b - a})
            reconstructed.extend(raw[offset:offset + b - a])
            copied += b - a
            position = b
        if position < len(data):
            segments.append({"Data": base64.b64encode(data[position:]).decode("ascii")})
            reconstructed.extend(data[position:])
        assert bytes(reconstructed) == data
        result["Sources"][kind] = {"Sha256": digest, "Size": len(raw), "Segments": segments}
        print(kind, "copied", copied, "literal", len(data) - copied, "segments", len(segments))
    args.output.parent.mkdir(parents=True, exist_ok=True)
    payload = json.dumps(result, separators=(",", ":")).encode("utf-8")
    args.output.write_bytes(gzip.compress(payload, mtime=0))
    print("ONNX", result["OutputSize"], result["OutputSha256"])
    print("Recipe", args.output.stat().st_size)


if __name__ == "__main__":
    main()
