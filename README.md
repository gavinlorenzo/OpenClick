# OpenClick

A lightweight, free, open-source autoclicker for Windows.

## Features

- **Flexible click interval** — hours / minutes / seconds / milliseconds, with an optional random offset to vary timing
- **Any mouse button** — left, right, or middle
- **Single or double click**
- **Repeat control** — click N times, or keep going until stopped
- **Position modes** — click at the current cursor position, or at a fixed point picked on screen
- **Fully customizable global hotkeys** — start/stop from anywhere, with modifier combos (e.g. `Ctrl+Shift+F6`)
- **Hold-to-click mode** — autoclicks while you physically hold the mouse button down
- **Macro record & playback** — record mouse and keyboard, replay with a speed multiplier and repeat count
- **Background clicking** — send clicks into unfocused windows via `PostMessage`, no focus stealing
- **Multiclicking** — click several windows, or several points in one window, on every tick
- **Persistent settings** — everything is saved between runs
- **Zero dependencies** — a single small executable, no installers, no runtimes to chase

## Getting started

Grab the latest build from [GitHub Releases](https://github.com/gavinlorenzo/OpenClick/releases), or build from source (requires the .NET 10 SDK on Windows):

```
dotnet build
dotnet run --project src/OpenClick
```

## Building a release

```
dotnet publish src/OpenClick -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true
```

## Hotkeys

| Key | Action |
|-----|--------|
| `F6` | Toggle clicker |
| `F7` | Toggle recording |
| `F8` | Toggle playback |

All hotkeys are rebindable in the **Settings** tab, including modifier combos like `Ctrl+Shift+F6`.

## Limitations & notes

- Background clicking uses `PostMessage`, so apps that read hardware input directly (many games, anything using raw input or DirectInput) will ignore it.
- Clicking into apps running as administrator requires running OpenClick as administrator too.
- Some antivirus tools flag autoclickers generically — the code is fully open here, so you can audit and build it yourself.
- Hold-to-click and global hotkeys use low-level Windows hooks.

## Architecture

OpenClick is a single WinForms app targeting `net10.0-windows`, with all Win32 access done via P/Invoke — no external NuGet packages. See [docs/SPEC.md](docs/SPEC.md) for the full specification, threading model, and module layout.

## License

[MIT](LICENSE)
