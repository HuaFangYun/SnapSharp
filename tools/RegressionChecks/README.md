Run from the repository root with the .NET 10 SDK on Windows:

```powershell
dotnet run --project tools/RegressionChecks/RegressionChecks.csproj -c Release
```

To run only the recording checks, using generated images without desktop, audio or camera capture:

```powershell
dotnet run --project tools/RecordingChecks/RecordingChecks.csproj -c Release
```

The standalone runner shares its engine checks with the full runner (`--recording-only`) and does not compile unrelated regression checks. These checks cover the recording library boundary, owner-thread command contract, rejected session inputs, non-overwriting output names, pause-excluding clock, GIF encode/decode, and synthetic native MP4 encoding at 15/30/60 fps. The standalone runner also loads the production resources to check the application's recording configuration, toolbar and setup layout. The full runner covers floating configuration visibility and audio selection. Hardware encoding, real capture and device behavior still require the opt-in recording capture checks or manual verification.

To run the data-folder and simulated download checks without opening UI fixtures:

```powershell
dotnet run --project tools/RegressionChecks/RegressionChecks.csproj -c Release -- --downloads-only
```

Download-source checks cover official defaults, domestic mirrors for all model catalogs, pinned versions, formula release assets, allowed redirects, and verified resume after switching sources. The full run also checks source persistence and the shared source selector across settings pages, including its lock during downloads.

Use `--download-ui-only` to check settings download success, cancellation, failure, and control restoration with a simulated download.

Model-cache checks cover legacy directories, complete model sets, corrupt/missing/unreadable files, SHA256 and MD5 checks, cancellation, and concurrent requests without duplicate downloads. Optional real model fixtures are copied to the test folder; the source fixtures and saved settings are not changed:

```powershell
dotnet run --project tools/RegressionChecks/RegressionChecks.csproj -c Release -- --downloads-only --model-cache-fixtures publish/models
```

The fixture folder should contain the current default PP-OCRv6 Small package, Tesseract, table and formula packages. These checks verify reuse across mirror changes, adding a language in place, rollback of a failed language extension, concurrent OCR installation, and corrupt dictionary recovery. The runner serves the verified local assets through simulated HTTP responses.

Data-folder checks cover the executable-folder default, legacy discovery, restart scheduling/cancellation, nested model paths and history indexes, preservation of unrelated files, destination conflicts, invalid metadata, and rollback when the location configuration cannot be committed. To run only these checks:

```powershell
dotnet run --project tools/RegressionChecks/RegressionChecks.csproj -c Release -- --data-directory-only
```

These checks also move a generated portable folder and verify every model/runtime path, nested cutout models, absolute external paths, legacy settings, independence from the working directory, portable data-location metadata, and migration out of and back into the executable folder. The full run additionally checks relative path display and validation in settings.

Run `dotnet run --project tools/ModelStorageChecks/ModelStorageChecks.csproj -c Release` to check model storage listing, recursive sizes, saved and unsaved path protection, nondefault cutout models, separate translation assets, deletion boundaries and link rejection. The isolated runner shares its checks with the full runner (`--model-storage-only`). Deletion checks use generated fixtures and an injected recycler; they do not delete downloaded models or change the system Recycle Bin. The isolated runner also renders the Chinese storage window at compact and normal widths under its temporary output directory.

The checks use generated files in `.artifacts/regression-checks` and simulated HTTP responses. They cover window client-area selection and wheel scope, negative screen coordinates, element-mode/Tab navigation, element hierarchy and duplicate filtering, stale/canceled/slow element lookups, window fallback, pixel-exact element crops, consecutive annotations, object selection, text/callout editing and toolbar commits, recording settings persistence, automatic output naming, configuration overlay visibility, default audio selection, download progress, retry, cancellation, resume, and integrity checks. Temporary WPF and WinForms fixture processes verify real cross-process UI Automation and disabled child-window detection beneath the screenshot overlay. They do not record the desktop, capture audio/camera content, download external models, or modify the user's saved settings.

An optional real-browser check opens a generated local HTML fixture in an isolated Chrome/Edge profile, with extensions disabled and no remote-debugging port. It verifies page-button and input detection beneath the overlay, then closes the test browser. It does not access the user's browser profile or open tabs:

```powershell
dotnet run --project tools/RegressionChecks/RegressionChecks.csproj -c Release -- --browser-elements
```

The browser check skips if neither supported browser executable is installed. Cross-monitor hardware, elevated target applications, and arbitrary third-party accessibility providers still require manual verification.

## Application update checks

Run `dotnet run --project tools/UpdateChecks/UpdateChecks.csproj -c Release` for isolated offline checks. The full regression runner includes these checks and supports `--updates-only` when it builds successfully.

Checks cover stable releases, normalized versions, Windows x64 package selection, GitHub SHA256 metadata/checksum fallback, bounded downloads, ZIP path/data protection, executable version validation and cancellation. Real helper fixtures check file replacement, preservation of settings/models, and rollback when a DLL is locked. The standalone runner also checks waiting for the parent process to exit and launching the installed executable. No checks contact GitHub or replace the real NimCap Screenshot installation.
