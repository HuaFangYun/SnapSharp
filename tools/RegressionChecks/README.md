Run from the repository root with the .NET 10 SDK on Windows:

```powershell
dotnet run --project tools/RegressionChecks/RegressionChecks.csproj -c Release
```

Data-folder checks cover the executable-folder default, legacy discovery, restart scheduling/cancellation, nested model paths and history indexes, preservation of unrelated files, destination conflicts, invalid metadata, and rollback when the location configuration cannot be committed. To run only these checks:

```powershell
dotnet run --project tools/RegressionChecks/RegressionChecks.csproj -c Release -- --data-directory-only
```

The checks use generated files in `.artifacts/regression-checks` and simulated HTTP responses. They cover window client-area selection and wheel scope, negative screen coordinates, element-mode/Tab navigation, element hierarchy and duplicate filtering, stale/canceled/slow element lookups, window fallback, pixel-exact element crops, consecutive annotations, object selection, text/callout editing and toolbar commits, recording settings persistence, automatic output naming, configuration overlay visibility, default audio selection, download progress, retry, cancellation, resume, and integrity checks. Temporary WPF and WinForms fixture processes verify real cross-process UI Automation and disabled child-window detection beneath the screenshot overlay. They do not record the desktop, capture audio/camera content, download external models, or modify the user's saved settings.

An optional real-browser check opens a generated local HTML fixture in an isolated Chrome/Edge profile, with extensions disabled and no remote-debugging port. It verifies page-button and input detection beneath the overlay, then closes the test browser. It does not access the user's browser profile or open tabs:

```powershell
dotnet run --project tools/RegressionChecks/RegressionChecks.csproj -c Release -- --browser-elements
```

The browser check skips if neither supported browser executable is installed. Cross-monitor hardware, elevated target applications, and arbitrary third-party accessibility providers still require manual verification.
