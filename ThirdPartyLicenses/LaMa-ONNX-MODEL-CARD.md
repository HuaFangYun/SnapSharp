# LaMa fixed FP32 ONNX export

Publisher: Carve. Upstream architecture: advimman/lama (Big-LaMa).

Source: https://huggingface.co/Carve/LaMa-ONNX/tree/c3c0c9e468934d62e79c329e35d82dd09ff8c444

File: lama_fp32.onnx, 208044816 bytes.
SHA256: 1faef5301d78db7dda502fe59966957ec4b79dd64e16f03ed96913c7a4eb68d6

The export publisher declares Apache-2.0 in its model card. The corresponding
license is included as Apache-2.0.txt. This is a community export, not a
universal contract for all LaMa weights. The export publisher's declaration
is recorded separately from the upstream code license.

Contract: float32 RGB image [1,3,512,512] in [0,1], float32 binary mask
[1,1,512,512] (1 means repair), float32 RGB output [1,3,512,512] in [0,255].
The app crops surrounding context, resizes for inference, and composites only
the original mask pixels, preserving source alpha and unmasked pixels.

Weights are downloaded on demand and are not included in the application.
