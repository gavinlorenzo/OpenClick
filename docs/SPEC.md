# OpenClick maintainer notes

OpenClick is a Windows WinForms application targeting `net10.0-windows`. It uses no external NuGet packages. Windows API calls are declared in `OpenClick.Native.NativeMethods`.

The source code is the behavior reference. Keep this file limited to facts that make the codebase easier to enter and update it when those facts change.

## Source map

| Area | Location |
| --- | --- |
| Application startup | `src/OpenClick/Program.cs` |
| Main window and UI wiring | `src/OpenClick/UI/MainForm.cs` |
| UI overlays and hotkey input | `src/OpenClick/UI/` |
| Settings and shared models | `src/OpenClick/Core/Models.cs` |
| Repeated clicking and targets | `src/OpenClick/Core/ClickEngine.cs`, `ClickTargets.cs` |
| Global hotkeys and hold mode | `src/OpenClick/Core/HotkeyManager.cs`, `HoldClickMonitor.cs` |
| Macro recording and playback | `src/OpenClick/Core/Recorder.cs`, `Player.cs` |
| Background-target picker | `src/OpenClick/Core/WindowPicker.cs` |
| Windows interop | `src/OpenClick/Native/NativeMethods.cs` |

## Runtime behavior worth preserving

- `ClickEngine` and `Player` use dedicated background threads. The UI marshals their events with `BeginInvoke` before changing controls.
- Low-level keyboard and mouse hooks are created on the UI thread. Keep their callback work small and keep hook delegates alive for the lifetime of the hook.
- The click engine and player request a 1 ms timer period only while active, then release it when they stop.
- Background clicks use `PostMessage` and do not activate their targets. Programs that consume direct hardware input can ignore them.
- Macro recording ignores injected input and the recording hotkey. Macro files are JSON with the `.ocmacro.json` extension.
- App settings are stored in `%APPDATA%\OpenClick\settings.json`.

## Build

Run these commands from the repository root on Windows with the .NET 10 SDK:

```powershell
dotnet build -c Release
dotnet publish src/OpenClick -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true
```

The GitHub Actions workflow builds pull requests and publishes a Windows x64 artifact for version tags.
