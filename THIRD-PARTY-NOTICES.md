# Third-party notices

Optional watermark repair downloads use the author-provided MI-GAN ONNX
pipeline (MIT; `ThirdPartyLicenses/MI-GAN-LICENSE.txt`) and the Carve LaMa
FP32 export (publisher-declared Apache-2.0; provenance and contract recorded
in `ThirdPartyLicenses/LaMa-ONNX-MODEL-CARD.md`). Weights are not bundled.
Optional Moebius weights use the pinned ONNX export described in
`ThirdPartyLicenses/Moebius-ONNX-MODEL-CARD.md`.
SLBR automatic detection downloads a pinned checkpoint on user confirmation
and reconstructs a verified ONNX with a bundled installation recipe. Complete
SLBR weights are not bundled. Provenance and upstream research-weight conditions
are recorded in `ThirdPartyLicenses/SLBR-MODEL-CARD.md`.

Complete license and upstream notice texts for binaries distributed with
SnapSharp are included in `ThirdPartyLicenses/`. The project file copies that
directory into normal build and publish output. `PACKAGE-NOTICES.md` maps each
distributed direct or transitive package to its accompanying material. The
root Apache-2.0 `LICENSE` covers SnapSharp itself; it does not replace a third
party's copyright notice or separate terms.

## WPFLocalizeExtension 3.10.0

- Project: https://github.com/XAMLMarkupExtensions/WPFLocalizationExtension
- License: Microsoft Public License (Ms-PL); retained in `ThirdPartyLicenses/WPFLocalizeExtension-LICENSE.txt`.
- Use: WPF resource bindings and runtime interface language switching.
- Transitive XAMLMarkupExtensions 2.1.3 is also Ms-PL; its license is retained separately. System.Management 7.0.0 retains its MIT license and upstream third-party notices.

## ScreenRecorderLib 7.0.1

- Project: https://github.com/sskodje/ScreenRecorderLib
- License: MIT (NuGet package LICENSE)
- Use: continuous Windows Graphics Capture, WASAPI input/loopback mixing, camera overlays and Windows Media Foundation H.264/AAC encoding
- Distribution: native mixed-mode x64 assembly; requires Microsoft Visual C++ x64 runtime and Windows Media Foundation. No FFmpeg binaries are included.
- Codec note: library licensing does not grant H.264/AAC patent rights. Product distributors must separately review codec licensing obligations for their use and markets.
- GIF palette/dither and streaming encoder are implemented in this repository; no Magick.NET dependency.

## CommunityToolkit.Mvvm

- Project: https://github.com/CommunityToolkit/dotnet
- License: MIT
- Use: observable editor state and commands

## Microsoft.ML.OnnxRuntime.DirectML 1.24.1

- Project: https://github.com/microsoft/onnxruntime
- License: MIT (package LICENSE; native dependencies have their own notices)
- Use: selectable local CPU / DirectML GPU OCR and foreground-mask inference. DirectML native dependencies are distributed by the official NuGet package and retain their own licenses/notices.
- Model weights are not bundled; optional user-triggered downloads are documented below.
- Microsoft.AI.DirectML 1.15.4: native DirectML.dll redistributable from Microsoft's NuGet, supplied alongside the x64 app. It is governed by Microsoft's separate DirectML distribution terms, not only the MIT source-code license. The original binary license, source-code license, and third-party notices accompany the app.

## ZXing.Net 0.16.11

- Project: https://github.com/micjahn/ZXing.Net
- License: Apache-2.0
- Use: offline QR and selected 1D barcode decoding

## Clowd.Clipboard.Wpf

- Project: https://github.com/clowd/Clowd.Clipboard
- License: MIT
- Use: robust WPF bitmap clipboard formats and clipboard interoperability

## SkiaSharp

- Project: https://github.com/mono/SkiaSharp
- License: MIT
- Use: image composition, blur, pixelation, scaling and scrolling-capture stitching

## MahApps.Metro.IconPacks.Lucide

- Project: https://github.com/MahApps/MahApps.Metro.IconPacks
- Icon set: https://lucide.dev
- License: MIT / ISC
- Use: consistent line icons in the screenshot toolbar

## H.NotifyIcon.Wpf 2.4.1

- Project: https://github.com/HavenDV/H.NotifyIcon
- License: MIT
- Use: WPF system-tray icon, context menu, and balloon notifications

## Vortice.Windows

- Project: https://github.com/amerkoleci/Vortice.Windows
- License: MIT
- Use: managed D3D11 and DXGI bindings for Desktop Duplication capture

## Packaged transitive dependencies

- Clowd.Clipboard 1.1.4; MIT.
- H.NotifyIcon 2.4.1 and H.GeneratedIcons.System.Drawing 2.4.1; MIT.
- MahApps.Metro.IconPacks.Core 6.2.1; MIT. Lucide icon data retains the ISC and Feather MIT notices.
- Microsoft.ML.OnnxRuntime.Managed 1.24.1; MIT and the ONNX Runtime third-party notices.
- SharpGen.Runtime / SharpGen.Runtime.COM 2.4.2-beta; MIT.
- SkiaSharp.NativeAssets.Win32 3.119.1; MIT plus Skia native third-party notices.
- System.Numerics.Tensors 9.0.0; MIT plus its third-party notices.
- Vortice.DirectX / Vortice.DXGI 3.8.3 and Vortice.Mathematics 2.1.0; MIT.
- The self-contained Windows x64 package includes .NET Runtime and Windows Desktop Runtime 10.0.14; their MIT licenses and upstream third-party notices are distributed in `ThirdPartyLicenses/`.
- Microsoft.Windows.SDK.NET.Ref 10.0.19041.57 supplies the packaged Windows SDK projection assembly under Microsoft's Windows SDK terms; the linked RTF license from the NuGet metadata accompanies the app.

## dotnet/wpf

- Project: https://github.com/dotnet/wpf
- License: MIT
- Reference: issue #4127, mixed-DPI window placement and `WM_DPICHANGED` behavior
- Use: physical-pixel placement of one WPF selection overlay per monitor

## ShareX and Flameshot

- Projects: https://github.com/ShareX/ShareX and https://github.com/flameshot-org/flameshot
- License: GPL-3.0
- Use: behavioral reference for region selection and the in-place annotation toolbar
- Note: independently implemented; no source code was copied

## RapidOCR / PaddleOCR model data (optional download)

- Projects: https://github.com/RapidAI/RapidOCR/tree/v1.3.24 and https://github.com/PaddlePaddle/PaddleOCR
- Distribution: rapidocr_onnxruntime 1.3.24 on PyPI, Apache-2.0
- Use: user-triggered download of PP-OCRv4 Chinese/English detection and recognition ONNX data; dictionary extracted from recognition metadata
- The wheel is treated only as a ZIP data container; Python code is neither installed nor executed. Version, source and pinned SHA256 are recorded alongside downloaded files. The complete Apache-2.0 text is copied next to the installed model.

## U-2-Net small / rembg model data (optional download)

- Projects: https://github.com/xuebinqin/U-2-Net and https://github.com/danielgatis/rembg
- License: Apache-2.0 (U-2-Net); rembg is MIT
- Distribution: https://github.com/danielgatis/rembg/releases/download/v0.0.0/u2netp.onnx
- Use: user-triggered download of data-only U2Net small weights, pinned SHA256 and ONNX input/output validation; source record plus the U-2-Net Apache-2.0 and rembg MIT texts are saved alongside the model. No Python installation or execution.

## BiRefNet model data (optional download)

- Upstream: https://github.com/ZhengPeng7/BiRefNet ; MIT, Copyright (c) 2024 ZhengPeng.
- General Lite, Portrait, and General assets are downloaded from the rembg release and retain the BiRefNet and rembg MIT texts.
- The pinned emrikol Matting export revision `0d58d809b3a360b44c556223d2f5812aeace9ba3` declares MIT in its model card; the card is retained with the model.
- The pinned PinkPixel HR Matting export revision `792518b9d4dfe9793891712677de25e554f948c6` declares MIT and supplies its own license; both the model card and license are retained with the model.

## PP-Structure SLANet / RapidTable (optional table recognition)

- Projects: https://github.com/PaddlePaddle/PaddleOCR and https://github.com/RapidAI/RapidTable ; Apache-2.0.
- Model: RapidAI/RapidTable v2.0.0 `ch_ppstructure_mobile_v2_SLANet.onnx`, 7,790,807 bytes, SHA256 ddfc6c97ee4db2a5e9de4de8b6a14508a39d42d228503219fdfebfac364885e3.
- `TableRecognition.cs` adapts the documented BGR preprocessing and structure decoding into C#; upstream preprocessing/decoding copyright (c) 2020 PaddlePaddle Authors, licensed under Apache-2.0 (https://www.apache.org/licenses/LICENSE-2.0). Local OCR matching and editable cell export run entirely on-device. Weights are downloaded only on request, not bundled. The Apache-2.0 text is copied next to an installed table model.

## LaTeX-OCR / RapidLaTeXOCR (optional formula recognition)

- Projects: https://github.com/lukas-blecher/LaTeX-OCR and https://github.com/RapidAI/RapidLaTeXOCR ; MIT. `FormulaRecognition.cs` adapts the preprocessing and encoder/decoder inference contract into C#, with deterministic greedy decoding and bounded generation.
- Weights and tokenizer are data-only assets from RapidLaTeXOCR release v0.0.0, individually pinned by asset ID, size and SHA256. No Python execution. Original model data is not bundled; download provenance and both MIT license texts are retained with installed files.
- The RapidLaTeXOCR wheel metadata declares Apache-2.0 while its LICENSE and upstream repository identify MIT; the implementation attribution follows those license files rather than assuming wheel metadata licenses every weight.

MIT License — Copyright (c) 2023 RapidAI; Copyright (c) 2021 Lukas Blecher.

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

## MyMemory (optional online service)

- Provider: https://mymemory.translated.net/ ; API: https://mymemory.translated.net/doc/spec.php
- Use: optional no-key translation preset; text is only sent after saved permission and per-request consent. Public quotas, provider terms and availability apply. No service software or model is bundled.

## Tesseract .NET / Tesseract OCR / tessdata_fast

- Wrapper: Tesseract NuGet 5.2.0, Charles Weld, https://github.com/charlesw/tesseract ; Apache-2.0
- Native engine: https://github.com/tesseract-ocr/tesseract ; Apache-2.0; native Tesseract and Leptonica binaries are supplied by the NuGet distribution and copied with the application (no local modifications).
- Leptonica: https://github.com/DanBloomberg/leptonica ; BSD-2-Clause; bundled native image processing dependency.
- Optional model data: https://github.com/tesseract-ocr/tessdata_fast/tree/87416418657359cb625c412a48b6e1d6d41c29bd ; Apache-2.0
- Use: selectable offline LSTM OCR; explicit user-triggered English/Simplified Chinese traineddata download with pinned revision, size and SHA256. Downloaded data remains separate from the application; provenance and the complete Apache-2.0 text are recorded alongside files. No Python code is downloaded or executed.

## GTranslate

- Package: GTranslate 2.4.0, d4n3436, https://github.com/d4n3436/GTranslate ; MIT
- Use: selectable Google online translation adapter; no package source modifications. No offline translation model is included. HTTP requests remain subject to saved opt-in, per-request consent, cancellation and response-size limits.
- The package license does not grant service availability, unlimited usage or exemption from provider terms. This is a third-party web adapter, not an official Google Cloud API client; requests may fail or be rate-limited. No automatic provider fallback.

## llama.cpp / Qwen2.5 1.5B Instruct GGUF (optional local translation)

- Runtime: https://github.com/ggml-org/llama.cpp/tree/b12396 ; MIT. Official Windows CPU or Vulkan x64 binary ZIP, fixed release b12396 and SHA256; the archive's dependency notices and the pinned llama.cpp MIT license are retained in the runtime directory. No source modifications.
- Model: https://huggingface.co/Qwen/Qwen2.5-1.5B-Instruct-GGUF ; Apache-2.0. Revision 91cad51170dc346986eccefdc2dd33a9da36ead9, qwen2.5-1.5b-instruct-q4_k_m.gguf (1,117,320,736 bytes), SHA256 6a1a2eb6d15622bf3c96857206351ba97e1af16c30d7a74ee38970e434e9407e.
- Explicit one-click download only; not bundled. The Qwen Apache-2.0 text is copied next to the model. Translation runs in a short-lived local child process, with an authenticated loopback listener, no remote inference provider. CPU/GPU are explicitly selected; no automatic provider/device fallback.
- Vulkan drivers and their licensing are supplied by the GPU vendor. Model and runtime licenses do not imply translation accuracy, paid API access, or service availability.

## Microsoft Visual C++ Redistributable / Media Feature Pack (optional system setup)

- Official runtime distribution: https://aka.ms/vs/17/release/vc_redist.x64.exe
- Use: user-triggered recording dependency setup. The runtime installer is downloaded directly from Microsoft's HTTPS hosts and checked using Windows Authenticode trust and the Microsoft Corporation publisher before execution. It is not bundled with the application.
- Media Feature Pack is installed on Windows N/KN via Windows DISM/Windows Update, not redistributed by SnapSharp. Installation requires Windows administrator authorization and can require a restart.
- These system components have Microsoft's own terms; the ScreenRecorderLib MIT license does not replace them. No installer or system-component source code has been modified.
# Moebius ONNX FP32 (optional image repair model)

Upstream: https://huggingface.co/hustvl/Moebius ; ONNX conversion:
https://huggingface.co/simonw/Moebius-ONNX at revision
5bf1ef5d2861ec01a727183a3f95dc64f352120e. Licensed Apache-2.0.
Weights are downloaded separately with per-file SHA256 validation.
See `ThirdPartyLicenses/Moebius-ONNX-MODEL-CARD.md` and `Apache-2.0.txt`.
