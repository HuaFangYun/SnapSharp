# Window layering checks

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
