# Stability checks

Run from the repository root with .NET 10 SDK on Windows:

```powershell
dotnet run --project tools/StabilityChecks/StabilityChecks.csproj -c Release
```

The runner uses its own directory under `.artifacts/stability-checks`, synthetic pixels and an embedded tiny ONNX model. It does not read screenshots, OCR history or downloaded models. It temporarily redirects the runner's data location and restores that configuration afterwards.

Checks cover:

- A real WPF button callback throws an exception through the Dispatcher; execution continues, a report is written, and exception messages and source filenames are excluded. The error dialog is replaced with an assertion callback to avoid blocking unattended execution.
- Unobserved task exceptions are recorded and marked observed; nested fatal exceptions are classified correctly.
- Hotkeys use either native registration or polling. Simulated held keys, modifier changes, release/rearm and suspension exercise production routing without injecting global keyboard input or taking over the user's shortcuts.
- Actual CPU ONNX inference crosses the same child-process pipe as the application. An invalid DirectML adapter falls back to CPU without changing settings. Cancellation is honored. Killing only a test worker leaves the parent alive and allows the next request to succeed.
- A corrupt model causes an actual managed worker exception; its short stack crosses IPC into diagnostics without exposing the model path, model contents, exception message or source filenames.

The runner needs permission to start child processes and use Windows named pipes. Synthetic checks do not establish stability of a particular GPU driver or large model. Check the visible crash dialog's copy button and real keyboard interaction manually.
