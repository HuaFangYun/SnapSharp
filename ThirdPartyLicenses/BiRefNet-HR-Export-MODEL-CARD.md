---
license: mit
pipeline_tag: image-segmentation
base_model: ZhengPeng7/BiRefNet_HR-matting
tags:
- background-removal
- image-matting
- image-segmentation
- onnx
- onnxruntime
- birefnet
- high-resolution
- soft-alpha
- transparency
- rgba
library_name: onnx
---

# BiRefNet HR Matting ONNX: High-Resolution Background Removal and Image Matting

This repository provides a self-contained ONNX export of [BiRefNet_HR-matting](https://huggingface.co/ZhengPeng7/BiRefNet_HR-matting), a high-resolution background removal and soft-alpha matting model trained by [Peng Zheng](https://github.com/ZhengPeng7).

BiRefNet HR Matting operates at a native 2048x2048 input resolution. It employs Bilateral Reference mechanisms to reconstruct fine details, hair strands, translucent boundaries, and complex edges.

## Model Summary

| Property | Details |
|---|---|
| Original Model | [ZhengPeng7/BiRefNet_HR-matting](https://huggingface.co/ZhengPeng7/BiRefNet_HR-matting) |
| Original Code | [github.com/ZhengPeng7/BiRefNet](https://github.com/ZhengPeng7/BiRefNet) |
| Paper | [Bilateral Reference for High-Resolution Dichotomous Image Segmentation (CAAI AIR 2024)](https://arxiv.org/abs/2401.03407) |
| Base Architecture | BiRefNet HR |
| Primary Task | High-resolution background removal, dichotomous segmentation, soft-alpha extraction |
| Training Resolution | 2048x2048 |
| Format | ONNX (self-contained model weights) |
| Available Variants | FP32 (889 MB), FP16 (450 MB), INT8 (1007 MB), UINT8 (1007 MB), Q4 (988 MB) |
| Input Tensor | `image`: `[1, 3, 2048, 2048]` (Float32, ImageNet normalized RGB) |
| Output Tensor | `alpha`: `[1, 1, 2048, 2048]` (Float32, Sigmoid activated, range `[0.0, 1.0]`) |
| Supported Execution Providers | CPU, CUDA, DirectML, CoreML, WebGPU |
| License | MIT |

## Available Quantizations

| File | Precision | File Size | Recommended Runtime | Notes |
|---|---|---|---|---|
| `model.onnx` | Float32 | 889 MB | High-precision reference | Base unquantized model |
| `model_fp16.onnx` | Float16 | 450 MB | CUDA, WebGPU, Apple Silicon, DirectML | Recommended. Halves memory footprint and accelerates 2D convolutions with zero loss in alpha detail |
| `model_int8.onnx` | Dynamic INT8 | 1007 MB | CPU servers | Quantizes linear projection layers |
| `model_uint8.onnx` | Dynamic UINT8 | 1007 MB | Specific CPU runtimes | Unsigned 8-bit dynamic quantization |
| `model_q4.onnx` | 4-bit block-wise | 988 MB | Edge memory | MatMul 4-bit weight packing |


## Benchmark Highlights

Trained at 2048x2048 resolution, BiRefNet HR Matting delivers high precision across established matting and segmentation test sets (evaluated in FP16 mode):

| Benchmark Dataset | Resolution | maxFm | wFmeasure | MAE | Smeasure | meanEm | maxBIoU |
|---|---|---|---|---|---|---|---|
| TE-AM-2k | 2048x2048 | 0.974 | 0.997 | 0.002 | 0.998 | 0.987 | 0.965 |
| TE-P3M-500-NP | 2048x2048 | 0.980 | 0.996 | 0.002 | 0.997 | 0.987 | 0.947 |

Key capabilities:
* **High-Resolution Fidelity:** Native 2048x2048 processing preserves delicate boundaries such as flyaway hair, netting, fine foliage, and glass.
* **Bilateral Reference Architecture:** Combines localization and detail-refinement branches to balance overall object structure with micro-boundary matting.
* **Direct Soft-Alpha Output:** Produces smooth grayscale alpha mattes suitable for transparent compositing without post-processing thresholds.

## Quickstart (Python)

### 1. Install Dependencies

```bash
pip install onnxruntime pillow numpy
# Or for NVIDIA GPU acceleration:
# pip install onnxruntime-gpu pillow numpy
```

### 2. Run Background Removal

```python
import numpy as np
import onnxruntime as ort
from PIL import Image

# 1. Load source image
img = Image.open("input.jpg").convert("RGB")
orig_w, orig_h = img.size

# 2. Resize to native model resolution (2048x2048) and normalize
resized = img.resize((2048, 2048), Image.Resampling.BILINEAR)
arr = np.array(resized, dtype=np.float32) / 255.0

mean = np.array([0.485, 0.456, 0.406], dtype=np.float32)
std = np.array([0.229, 0.224, 0.225], dtype=np.float32)
norm = (arr - mean) / std

# 3. Format tensor to shape [1, 3, 2048, 2048]
tensor = np.transpose(norm, (2, 0, 1))[np.newaxis, ...].astype(np.float32)

# 4. Run ONNX inference (use model_fp16.onnx for GPU acceleration)
session = ort.InferenceSession("model_fp16.onnx", providers=["CUDAExecutionProvider", "CPUExecutionProvider"])
# The output tensor already contains Sigmoid activation within the ONNX graph
alpha_raw = session.run(["alpha"], {"image": tensor})[0]

# 5. Extract alpha, clamp, and resize to original image dimensions
alpha_2d = np.squeeze(alpha_raw)
alpha_uint8 = (np.clip(alpha_2d, 0.0, 1.0) * 255.0).round().astype(np.uint8)
alpha_mask = Image.fromarray(alpha_uint8, mode="L").resize(
    (orig_w, orig_h), Image.Resampling.BILINEAR
)

# 6. Compose transparent RGBA image and save
cutout = img.convert("RGBA")
cutout.putalpha(alpha_mask)
cutout.save("output.png")
```

## Command-Line Usage

This repository includes a standalone CLI utility: [`infer.py`](infer.py).

### Single Image

```bash
# Generate transparent cutout (output defaults to <name>_cutout.png)
python infer.py --image photo.jpg

# Specify custom output path
python infer.py --image photo.jpg --output cutout.png

# Run on GPU via CUDA
python infer.py --image photo.jpg --provider cuda

# Save only the grayscale alpha matte mask
python infer.py --image photo.jpg --mask-only --output mask.png
```

### Batch Processing

```bash
# Process all supported images in a directory
python infer.py --dir ./input_images --output-dir ./cutouts

# Save only masks in batch mode
python infer.py --dir ./input_images --output-dir ./masks --mask-only
```

## Technical Details

### Input Specification
* **Name:** `image`
* **Shape:** `[1, 3, 2048, 2048]`
* **Data Type:** Float32
* **Color Order:** RGB
* **Normalization:** ImageNet statistics
  * Mean: `[0.485, 0.456, 0.406]`
  * Standard Deviation: `[0.229, 0.224, 0.225]`
  * Formula: `(pixel_value / 255.0 - mean) / std`

### Output Specification
* **Name:** `alpha`
* **Shape:** `[1, 1, 2048, 2048]`
* **Data Type:** Float32
* **Activation:** Sigmoid (values are in range `[0.0, 1.0]`)
  * `0.0`: Background
  * `1.0`: Foreground
  * `0.0 < alpha < 1.0`: Soft edges, hair, semitransparent materials

## Upstream Attribution

* **Original BiRefNet Model:** Developed by [Peng Zheng](https://github.com/ZhengPeng7) et al.
  * Hugging Face Model: [ZhengPeng7/BiRefNet_HR-matting](https://huggingface.co/ZhengPeng7/BiRefNet_HR-matting)
  * GitHub Repository: [github.com/ZhengPeng7/BiRefNet](https://github.com/ZhengPeng7/BiRefNet)
  * Demo Space: [Hugging Face Spaces - BiRefNet Demo](https://huggingface.co/spaces/ZhengPeng7/BiRefNet_demo)

### Citation

```bibtex
@article{zheng2024birefnet,
  title={Bilateral Reference for High-Resolution Dichotomous Image Segmentation},
  author={Zheng, Peng and Gao, Dehong and Fan, Deng-Ping and Liu, Li and Laaksonen, Jorma and Ouyang, Wanli and Sebe, Nicu},
  journal={CAAI Artificial Intelligence Research},
  volume={3},
  pages={9150038},
  year={2024},
  url={https://arxiv.org/abs/2401.03407}
}
```

## License

This distribution and upstream BiRefNet are released under the [MIT License](LICENSE).
