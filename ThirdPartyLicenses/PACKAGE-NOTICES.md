# Packaged third-party licenses

This directory contains the license and notice texts that accompany the
third-party binaries distributed with SnapSharp. The root
`THIRD-PARTY-NOTICES.md` describes how each dependency is used.
The offline license self-test requires the self-contained runtime version to
match the 10.0.14 notices retained here. A runtime update therefore requires
an explicit notice refresh instead of silently distributing mismatched files.

| Distributed component | Version | License material in this directory |
| --- | --- | --- |
| .NET Runtime | 10.0.14 | `DotNet-Runtime-LICENSE.txt`, `DotNet-Runtime-THIRD-PARTY-NOTICES.txt` |
| Windows Desktop Runtime / WPF | 10.0.14 | `WindowsDesktop-Runtime-LICENSE.txt`, `WindowsDesktop-Runtime-THIRD-PARTY-NOTICES.txt` |
| Microsoft.Windows.SDK.NET.Ref projection assembly | 10.0.19041.57 | `Microsoft-Windows-SDK-LICENSE.rtf` |
| ScreenRecorderLib | 7.0.1 | `ScreenRecorderLib-LICENSE.txt` |
| CommunityToolkit.Mvvm | 8.4.0 | `CommunityToolkit.Mvvm-LICENSE.md`, `CommunityToolkit.Mvvm-THIRD-PARTY-NOTICES.txt` |
| Microsoft.ML.OnnxRuntime.DirectML | 1.24.1 | `ONNXRuntime-LICENSE.txt`, `ONNXRuntime-THIRD-PARTY-NOTICES.txt` |
| Microsoft.AI.DirectML | 1.15.4 | `DirectML-LICENSE.txt`, `DirectML-LICENSE-CODE.txt`, `DirectML-THIRD-PARTY-NOTICES.txt` |
| SkiaSharp / Skia native assets | 3.119.1 | `SkiaSharp-LICENSE.txt`, `SkiaSharp-Native-THIRD-PARTY-NOTICES.txt` |
| System.Numerics.Tensors | 9.0.0 | `System.Numerics.Tensors-LICENSE.txt`, `System.Numerics.Tensors-THIRD-PARTY-NOTICES.txt` |
| Clowd.Clipboard / Clowd.Clipboard.Wpf | 1.1.4 | `Clowd.Clipboard-LICENSE.txt` |
| MahApps.Metro.IconPacks.Core / Lucide | 6.2.1 | `MahApps.IconPacks-LICENSE.txt`, `Lucide-LICENSE.txt` |
| H.NotifyIcon / H.NotifyIcon.Wpf / H.GeneratedIcons.System.Drawing | 2.4.1 | `H.NotifyIcon-LICENSE.md` |
| Vortice.Direct3D11 / DirectX / DXGI | 3.8.3 | `Vortice.Windows-LICENSE.txt` |
| Vortice.Mathematics | 2.1.0 | `Vortice.Windows-LICENSE.txt` |
| SharpGen.Runtime / SharpGen.Runtime.COM | 2.4.2-beta | `SharpGenTools-LICENSE.txt` |
| GTranslate | 2.4.0 | `GTranslate-LICENSE.txt` |
| WPFLocalizeExtension | 3.10.0 | `WPFLocalizeExtension-LICENSE.txt` (Ms-PL) |
| XAMLMarkupExtensions | 2.1.3 | `XAMLMarkupExtensions-LICENSE.txt` (Ms-PL) |
| System.Management | 7.0.0 | `System.Management-LICENSE.txt`, `System.Management-THIRD-PARTY-NOTICES.txt` |
| ZXing.Net | 0.16.11 | `Apache-2.0.txt` |
| Tesseract .NET wrapper and native Tesseract | 5.2.0 | `Apache-2.0.txt` |
| Leptonica native library | Tesseract 5.2.0 package dependency | `Leptonica-LICENSE.txt` |

The runtime license files come from the official 10.0.14 Windows x64 runtime
NuGet packages. The WPF third-party notices come from the corresponding
Microsoft source revision [`0427b9abbead3fa64c6655eb4290c537397131fa`](https://github.com/dotnet/dotnet/blob/0427b9abbead3fa64c6655eb4290c537397131fa/src/wpf/THIRD-PARTY-NOTICES.TXT).

Model and optional runtime licenses retained during user-triggered installs:

| Download | License material |
| --- | --- |
| RapidOCR / PaddleOCR, tessdata_fast, SLANet / RapidTable, Qwen2.5 | `Apache-2.0.txt` |
| U-2-Net assets distributed by rembg | `U-2-Net-LICENSE.txt`, `rembg-LICENSE.txt` |
| BiRefNet assets distributed by rembg | `BiRefNet-LICENSE.txt`, `rembg-LICENSE.txt` |
| emrikol BiRefNet Matting ONNX, revision `0d58d809b3a360b44c556223d2f5812aeace9ba3` | `BiRefNet-LICENSE.txt`, `BiRefNet-Matting-Export-MODEL-CARD.md` |
| PinkPixel BiRefNet HR Matting ONNX, revision `792518b9d4dfe9793891712677de25e554f948c6` | `BiRefNet-LICENSE.txt`, `BiRefNet-HR-Export-LICENSE.txt`, `BiRefNet-HR-Export-MODEL-CARD.md` |
| LaTeX-OCR / RapidLaTeXOCR | `LaTeX-OCR-LICENSE.txt`, `RapidLaTeXOCR-LICENSE.txt` |
| llama.cpp b12396 | `llama.cpp-LICENSE.txt` |

The model license files are copied next to the installed model or runtime.
Model provenance, fixed revision, file size, and checksum remain recorded in
the generated `source.txt` or `verified-files.json` files.
