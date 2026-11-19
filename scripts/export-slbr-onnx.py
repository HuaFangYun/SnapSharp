"""Export the official SLBR checkpoint to SnapSharp's fixed, named ONNX contract.

Development-only tool; the desktop app does not require Python or PyTorch.
Use a local checkout of bcmi/SLBR-Visible-Watermark-Removal at commit
47c665f1855ab6624cd52b28cefa797a9c8b96f7 and the official CLWD checkpoint.
No model/code is downloaded, and no source checkout is modified by this script.
"""
import argparse
import hashlib
import json
from pathlib import Path
import sys
import types


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--checkpoint", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--no-constant-folding", action="store_true",
                        help="Keep checkpoint tensors unchanged for the native installer recipe.")
    args = parser.parse_args()
    import torch
    import onnx

    source = args.source.resolve()
    # Load only inference modules. Avoid training dependencies and __init__ side effects.
    for package in ("src", "src.utils", "src.networks"):
        module = types.ModuleType(package)
        module.__path__ = [str(source.joinpath(*package.split(".")))]
        sys.modules[package] = module
    for name in ("src.utils.model_init", "src.networks.blocks", "src.networks.resunet"):
        path = source.joinpath(*name.split(".")).with_suffix(".py")
        text = path.read_text(encoding="utf-8")
        # These imports are unused by the official inference modules.
        text = "\n".join(line for line in text.splitlines() if line.strip() not in
                         {"import torchvision", "import cv2", "import scipy.stats as st"})
        module = types.ModuleType(name)
        module.__file__ = str(path)
        sys.modules[name] = module
        exec(compile(text, str(path), "exec"), module.__dict__)

    from src.networks.resunet import SLBR
    options = types.SimpleNamespace(bg_mode="res_mask", mask_mode="res", k_center=2,
                                    use_refine=True, k_refine=3, k_skip_stage=3,
                                    sim_metric="cos", project_mode="simple")
    network = SLBR(args=options, shared_depth=1, blocks=3, long_skip=True).eval()
    # Refuse arbitrary checkpoint pickle execution.
    checkpoint = torch.load(args.checkpoint, map_location="cpu", weights_only=True)
    state = checkpoint["state_dict"]
    state = {key.removeprefix("module.").replace(".module.", "."): value for key, value in state.items()}
    network.load_state_dict(state, strict=True)

    class Export(torch.nn.Module):
        def __init__(self):
            super().__init__()
            self.network = network

        def forward(self, image):
            backgrounds, masks, _ = self.network(image)
            return backgrounds[0], masks[0]

    exported = Export().eval()
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with torch.no_grad():
        torch.onnx.export(exported, torch.zeros(1, 3, 256, 256), str(args.output),
                          input_names=["image"], output_names=["background", "mask"],
                          opset_version=17, dynamo=False, do_constant_folding=not args.no_constant_folding)
    onnx.checker.check_model(str(args.output))
    metadata = {
        "architecture": "SLBR", "source_commit": "47c665f1855ab6624cd52b28cefa797a9c8b96f7",
        "checkpoint_sha256": hashlib.sha256(args.checkpoint.read_bytes()).hexdigest(),
        "onnx_sha256": hashlib.sha256(args.output.read_bytes()).hexdigest(),
        "input": "image float32 RGB [1,3,256,256] in [0,1]",
        "mask": "mask float32 [1,1,256,256] probability, 1 = watermark",
        "note": "Verify upstream code and checkpoint distribution terms before publishing weights.",
    }
    args.output.with_suffix(".json").write_text(json.dumps(metadata, indent=2), encoding="utf-8")
    print(json.dumps(metadata, indent=2))


if __name__ == "__main__":
    main()
