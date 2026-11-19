# Moebius ONNX FP32

Upstream model: https://huggingface.co/hustvl/Moebius
ONNX conversion: https://huggingface.co/simonw/Moebius-ONNX
Pinned conversion revision: 5bf1ef5d2861ec01a727183a3f95dc64f352120e
License: Apache-2.0; see Apache-2.0.txt.

Authors: Kangsheng Duan, Ziyang Xu, Wenyu Liu, Xiaohu Ruan, Xiaoxin Chen,
and Xinggang Wang. Moebius: 0.2B Lightweight Image Inpainting Framework
with 10B-Level Performance (2026), arXiv:2606.19195.

The ONNX conversion is credited to simonw/Moebius-ONNX. Three FP32 graphs
are required: unet.onnx, vae_encoder.onnx, vae_decoder.onnx. NimCap Screenshot pins
the SHA256 and exact size of each graph in WatermarkModels.cs and downloads
the weights separately. Both download sources must deliver identical bytes.

Runtime contract: 512x512 images, VAE scale 0.13025, learned conditional
IDs 0..9 and unconditional IDs 10..19, DDIM scaled-linear beta schedule
0.00085..0.012, 20 inference steps at strength 0.99 (19 denoising steps),
classifier-free guidance 7.5. Mask white means replace. NimCap Screenshot preserves
original pixels and alpha outside the user mask.

This optional detail candidate synthesizes image content. Its result is
not a reconstruction guarantee or a claim of superiority over LaMa.
