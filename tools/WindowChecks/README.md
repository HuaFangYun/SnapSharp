# Window layering checks

Run from the repository root on Windows:

```powershell
dotnet run --project tools/WindowChecks/WindowChecks.csproj -p:Platform=x64
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
- Pixel movement/resizing at simulated 100%, 150% and 200% scales, monitor edge clamping,
  precision snap bypass, original image output, capture rulers and the desktop ruler.
- Guides default off, independent persistence of enabled/disabled states, restoring
  preferences in a new capture and synchronization between monitors.
- Fixed capture shortcuts R/X, top hint text and toolbar tooltips, removal of auxiliary
  settings and movement hints that follow focus and selection constraints.
- Desktop ruler distances relative to its own zero after moving/resizing, including
  the far corner matching the ruler's physical pixel dimensions.
- All guide strings localized in every language pack, compiled Simplified/Traditional
  Chinese resource checks and rendered Chinese capture/settings previews.

The suite saves capture and ruler previews under its temporary output directory.

Reviewed guide translations live in `scripts/capture-guide-translations.json`.
Apply them with `python scripts/apply-capture-guide-translations.py` and validate
all packs with `python scripts/validate-language-packs.py`. The translation generator
also preserves these reviewed strings.

The print dialog uses the same temporary topmost suppression but requires manual
validation with an installed printer. Recording/scrolling indicators and screen-word
popups keep their existing nonactivating behavior; they are excluded from automatic
dialog ownership.
