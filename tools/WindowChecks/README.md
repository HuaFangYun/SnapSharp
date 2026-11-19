# Window layering checks

One-click redaction rules and screenshot/pin integration (synthetic OCR input, no model required):

```powershell
dotnet run --project tools/WindowChecks/WindowChecks.csproj -p:Platform=x64 -- --redaction-only
```

Checks split numbers, multiline fields, source bounds, mosaic pixels, toolbar entries, pin preview/output, batch undo/redo and cancellation of obsolete editing sessions.

Mixed DPI coordinates, portrait rotation, synthetic adapter composition, cross-screen
production crop, five-scale pixel nudging and guide persistence (no desktop capture):

```powershell
dotnet run --project tools/WindowChecks/WindowChecks.csproj -c Release -p:Platform=x64 -- --dpi-only
```

This checks all 25 ordered pairs of 100/125/150/175/200% scaling in landscape/portrait
and positive/negative desktop origins, DXGI 90/180/270 degree rotation, exact output
buffer composition and a single continuous selection overlay. `monitor-dpi.txt` records
only local display bounds and effective DPI. Synthetic adapters do not establish
Intel/AMD/NVIDIA driver compatibility; physical mixed-monitor dragging remains manual.

Run from the repository root on Windows:

```powershell
dotnet run --project tools/WindowChecks/WindowChecks.csproj -p:Platform=x64
```

Run crosshair rendering, magnifier, capture precision and guide preference checks
without the live desktop sampling check:

```powershell
dotnet run --project tools/WindowChecks/WindowChecks.csproj -p:Platform=x64 -- --precision-only
```

The checks open and close real WPF windows, inspecting native Z-order and modal
enablement. They do not register global hotkeys, run OCR, download models or save
application preferences. Temporary settings live in `.artifacts/window-checks`.

Run only document extraction and recognition navigation/layout checks:

```powershell
dotnet run --project tools/WindowChecks/WindowChecks.csproj -p:Platform=x64 -- --recognition-only
```

This checks classification and extraction with synthetic OCR blocks (including
ambiguous types, split labels/values, multiline addresses and local checksums),
ID front/back/both modes with either vertical order, all six navigation modes,
field editing/location and 980/1240-pixel layouts. Image rotation/reflection is
checked pixel by pixel; busy-state locking and stale-result clearing are checked.
Business fixtures cover fourteen fields, horizontal columns/vertical layouts,
branch labels, code below its label, empty fields, multiline scope/address and
footer issue dates without absorbing text from the other column.
Five additional layouts are manually transcribed from the supplied business-license
examples to isolate extraction errors: widely separated label glyphs, actual blank
fields, short printed codes, pagination versus certificate suffixes, separated date
digits, wrapped printed authority and field boundaries. These fixtures exercise
extraction from supplied text and coordinates.
Rendered previews are saved in the temporary output directory.

Add `--recognition-model-dir <folder>` to run the full local OCR and row/number
retry path on generated ID and business-license layouts. The folder must already contain
`detection.onnx`, `recognition.onnx` and a matching `dictionary.txt`; no downloads
are performed. This checks all six front-side fields and eight combined fields
with both vertical orders, plus horizontal/vertical business field extraction.

For original local images, run:

```powershell
dotnet run --project tools/WindowChecks/WindowChecks.csproj -p:Platform=x64 -- --document-images <image-folder> --recognition-model-dir <model-folder>
```

This processes the input files without changing them. Each image produces text
files containing extracted fields, original/retry OCR blocks with coordinates and
confidence, and field evidence/review status in the printed artifact directory.
Use the saved text and coordinates to reproduce and inspect field extraction.

For the public document sample audit (requires network only for downloading):

```powershell
python scripts/download-document-samples.py
dotnet run --project tools/WindowChecks/WindowChecks.csproj -p:Platform=x64 -- --document-images .artifacts/document-web-audit/images --recognition-model-dir <model-folder>
python scripts/review-document-samples.py <printed-output-folder>
```

The download manifest records URLs and SHA-256 values for 22 Tencent demonstration
images across all supported types, plus two supplemental GitHub images (one upside
down and one a screenshot). `document-sample-reference.json` contains a manually
inspected **subset** of fields for the 22 standard samples. The comparison script
checks only those listed entries. The two supplemental images are retained as
additional extraction inputs.

Diagnostics also include `photo-*-blocks.json` to preserve OCR input and output
independently of extraction. To verify a parser change without rerunning a model:

```powershell
dotnet run --project tools/WindowChecks/WindowChecks.csproj -p:Platform=x64 -- --document-replay <previous-output-folder>
python scripts/review-document-samples.py <new-printed-output-folder>
```

This replays the actual cached OCR text/coordinates/confidence, not expected field
values. For new preprocessing or model changes, rerun the original image command.

Coverage:

- Production controller route: topmost pin → OCR → OCR settings → nested confirmation.
- Obstructing unrelated pins and restoration of the original topmost preferences.
- Save image, canvas expansion, watermark, text input, recording setup and privacy consent.
- Native owner HWND forwarding for WinForms color dialogs and WPF file/folder dialogs,
  using probe dialogs in place of opening OS pickers.
- Active-window fallback for tray commands, hidden owners, dialog failures and pins
  closed during a modal dialog.
- Capture shape/aspect-ratio availability and switching from element capture to region capture.
- Native window regions and DWM corner preferences, dialog corner transparency through
  element editing and PNG export, square/maximized windows, rectangular child elements,
  manual region captures and clipped windows on monitors with negative coordinates.
- Pixel movement/resizing at simulated 100%, 150% and 200% scales, monitor edge clamping,
  precision snap bypass, original image output, capture rulers and the desktop ruler.
- Guides default off, independent persistence of enabled/disabled states, restoring
  preferences in a new capture and synchronization between monitors.
- Fixed capture shortcuts R/X, top hint text and toolbar tooltips, removal of auxiliary
  settings and movement hints that follow focus and selection constraints.
- Desktop ruler distances relative to its marked inset zero after moving/resizing,
  excluding scale bands from measurement and matching endpoints to effective pixel dimensions.
- Independent linear/box/two-point layers and three-point angles, axis constraints,
  guide hiding and snapping, image-edge detection, cancellation and clearing.
- Readout placement for horizontal, vertical and diagonal measurements, angle arcs,
  endpoints, projection lines, canvas edges, multiple labels and magnifier exclusion;
  actual compact-window labels are checked against every measurement line.
- Ruler auto-copy defaults, persistence on reopening, manual copy, four result formats,
  commit-only triggers, busy-clipboard recovery and transient footer notifications.
  An injected writer verifies copy behavior without changing the user's clipboard.
- Live magnifier sampling against a known background window, verifying the ruler is
  excluded from the sample. This check requires desktop screen-capture access.
- Magnifier crosshair contrast on white/black/color backgrounds, center color preservation,
  off-center sampled-pixel tracking and desktop-edge positioning, with rendered previews.
- Exactly one black and one white device pixel for capture/ruler/magnifier/annotation
  crosshairs and pixel frames at 100/125/150/200/300% DPI, zoom and fractional offsets;
  custom cursor arm thickness and hotspot position.
- All guide strings localized in every language pack, compiled Simplified/Traditional
  Chinese resource checks and rendered Chinese capture/settings previews.

The suite saves capture and ruler previews under its temporary output directory.

Reviewed guide translations live in `scripts/capture-guide-translations.json`
and `scripts/desktop-ruler-translations.json`.
Apply them with `python scripts/apply-capture-guide-translations.py` and validate
all packs with `python scripts/validate-language-packs.py`. The translation generator
also preserves these reviewed strings.

The print dialog uses the same temporary topmost suppression but requires manual
validation with an installed printer. Recording/scrolling indicators and screen-word
popups keep their existing nonactivating behavior; they are excluded from automatic
dialog ownership.
