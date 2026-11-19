# SLBR automatic detector setup

Model/paper: Visible Watermark Removal via Self-calibrated Localization
and Background Refinement, Jing Liang, Li Niu, Fengjun Guo, Teng Long,
and Liqing Zhang, ACM MM 2021.
Upstream: https://github.com/bcmi/SLBR-Visible-Watermark-Removal
Architecture revision: 47c665f1855ab6624cd52b28cefa797a9c8b96f7.

Official CLWD checkpoint linked by the upstream README:
https://drive.google.com/file/d/1uTCzubnWZtu3HIXaK8xsXX-7x302ss13/view
Size: 85,707,015 bytes.
SHA256: be5c0a89513803c8ce56b1768e0c6296900f6ef76df8cbda12900324f9b1caf9.

Community checkpoint used by the China mirror option:
https://huggingface.co/CuiMuxuan/moonshine-models/tree/43d683e99c44c77ada2e653009629d23069b5ce3
File: slbr.pth.tar; size: 85,782,395 bytes.
SHA256: f3984bd73e8eff5bfd69ad4786788c049a934fc8619821e4d9b9605c31a5d9b0.
The model's trainable parameters match the official checkpoint. Some BN
buffers differ. The native installation recipe retains official inference
statistics and copies matching tensors from the pinned container, so both
sources reconstruct the exact same official-parameter ONNX.

Installed ONNX: 86,773,294 bytes.
SHA256: c151347105c2ecfdba1887223bc4b8732ce5d5b828ff2005d5086174c0faa0ec.
Input: image float32 RGB [1,3,256,256], range [0,1].
Outputs: background float32 RGB and mask float32 [1,1,256,256], range [0,1].

Installation never executes Python, PyTorch, pickle or downloaded code.
It verifies the checkpoint, copies only fixed byte ranges and embedded graph
constants, verifies the resulting ONNX and validates its runtime contract.
The compressed recipe includes graph metadata and the official BN statistics;
the complete checkpoint and ONNX weights are downloaded/generated separately.

The cited upstream revision contains no explicit general redistribution
license for the SLBR code/checkpoint. This attribution records provenance;
it does not assert an MIT/Apache license or grant additional rights. The
download source and original authors' conditions continue to apply. Review
these terms before redistributing a build containing the generated recipe.
