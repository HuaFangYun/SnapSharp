Run from the repository root with the .NET 10 SDK on Windows:

```powershell
dotnet run --project tools/RegressionChecks/RegressionChecks.csproj -c Release
```

The checks use generated files in `.artifacts/regression-checks` and simulated HTTP responses. They cover window client-area selection and wheel scope, negative screen coordinates, consecutive annotations, object selection, text/callout editing and toolbar commits, recording settings persistence, automatic output naming, configuration overlay visibility, default audio selection, download progress, retry, cancellation, resume, and integrity checks. They do not record the desktop, capture audio/camera content, download external models, or modify the user's saved settings.
