# OpenClick — Master Specification

OpenClick is a lightweight, open-source Windows autoclicker. Single WinForms app, .NET 10 (`net10.0-windows`), C#, zero external NuGet dependencies. All Win32 access via P/Invoke in `OpenClick.Native.NativeMethods`.

**Namespaces:** `OpenClick.Native`, `OpenClick.Core`, `OpenClick.UI`.
**Nullable enabled. `LangVersion` latest. Warnings must be zero.**

## Feature summary

1. Autoclick: interval (h/m/s/ms), optional random offset, mouse button (L/R/M), single/double click, repeat N times or until stopped, click at current cursor position or fixed X,Y (with Pick-location overlay), customizable start/stop hotkey with modifier combos, hold-to-click mode.
2. Record & playback of mouse+keyboard macros with speed multiplier and repeat.
3. Background clicking: PostMessage-based clicks into non-focused windows.
4. Multiclick: click several background targets (different windows and/or several points in one window) each tick.

## Threading model

- `ClickEngine`, `Player` run their loops on dedicated background threads.
- All low-level hooks (`WH_KEYBOARD_LL`=13, `WH_MOUSE_LL`=14) are installed from the UI thread (it owns a message pump). Hook callbacks must be fast; raise events synchronously from the callback — subscribers (MainForm) marshal to UI with `BeginInvoke` when needed and hand heavy work off.
- Keep hook delegate instances in fields to prevent GC collection.
- Worker threads raise events on the worker thread; UI subscribers marshal.
- While a click/playback loop runs, call `winmm!timeBeginPeriod(1)` and matching `timeEndPeriod(1)` when it stops (engine owns this).

---

## File map & ownership

| File | Contents |
|---|---|
| `src/OpenClick/Native/NativeMethods.cs` | All P/Invoke, structs, constants |
| `src/OpenClick/Core/Models.cs` | Enums, `ClickSettings`, `HotkeyCombo`, `WindowTargetInfo`, `MacroEvent`, `MacroScript`, `AppSettings`, `SettingsStore` |
| `src/OpenClick/Core/ClickEngine.cs` | Click loop engine |
| `src/OpenClick/Core/ClickTargets.cs` | `IClickTarget`, `ForegroundTarget`, `WindowTarget`, `MultiTarget` |
| `src/OpenClick/Core/HotkeyManager.cs` | Global hotkey combos via LL keyboard hook |
| `src/OpenClick/Core/HoldClickMonitor.cs` | Hold-to-click via LL mouse hook |
| `src/OpenClick/Core/WindowPicker.cs` + `src/OpenClick/UI/WindowPickerOverlay.cs` | Pick a background target window/point |
| `src/OpenClick/Core/Recorder.cs` | Macro recorder (LL hooks) |
| `src/OpenClick/Core/Player.cs` | Macro playback (SendInput) |
| `src/OpenClick/UI/MainForm.cs` | Main window, 4 tabs, wiring |
| `src/OpenClick/UI/PickLocationOverlay.cs` | Fullscreen point picker |
| `src/OpenClick/UI/HotkeyCaptureBox.cs` | TextBox-like control that captures a combo |
| `src/OpenClick/Program.cs` | Entry point |

Each file lists an owner during the build; do not modify files you don't own, and never revert others' edits.

---

## Shared contracts (exact — code against these verbatim)

### Enums (Models.cs)

```csharp
namespace OpenClick.Core;

public enum MouseButton { Left, Right, Middle }
public enum ClickType { Single, Double }
public enum RepeatMode { UntilStopped, Count }
public enum PositionMode { Current, Fixed }
public enum MacroEventType { MouseMove, MouseDown, MouseUp, MouseWheel, KeyDown, KeyUp }
```

### ClickSettings (Models.cs) — plain mutable class, JSON-serializable

```csharp
public sealed class ClickSettings
{
    public int IntervalHours { get; set; }          // >= 0
    public int IntervalMinutes { get; set; }        // >= 0
    public int IntervalSeconds { get; set; }        // >= 0
    public int IntervalMilliseconds { get; set; } = 100;
    public bool RandomOffsetEnabled { get; set; }
    public int RandomOffsetMs { get; set; } = 20;   // +/- range
    public MouseButton Button { get; set; } = MouseButton.Left;
    public ClickType ClickType { get; set; } = ClickType.Single;
    public RepeatMode RepeatMode { get; set; } = RepeatMode.UntilStopped;
    public int RepeatCount { get; set; } = 100;     // used when RepeatMode.Count
    public PositionMode PositionMode { get; set; } = PositionMode.Current;
    public int FixedX { get; set; }
    public int FixedY { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public int TotalIntervalMs => Math.Max(1,
        ((IntervalHours * 60 + IntervalMinutes) * 60 + IntervalSeconds) * 1000 + IntervalMilliseconds);

    /// Base interval +/- random offset (uniform in [-RandomOffsetMs, +RandomOffsetMs]), clamped >= 1.
    public int GetEffectiveIntervalMs(Random rng) =>
        RandomOffsetEnabled
            ? Math.Max(1, TotalIntervalMs + rng.Next(-RandomOffsetMs, RandomOffsetMs + 1))
            : TotalIntervalMs;

    public ClickSettings Clone() => (ClickSettings)MemberwiseClone();
}
```

### HotkeyCombo (Models.cs)

```csharp
public sealed class HotkeyCombo
{
    public int KeyCode { get; set; }   // virtual-key code of the main key (e.g., 0x75 = F6)
    public bool Ctrl { get; set; }
    public bool Shift { get; set; }
    public bool Alt { get; set; }
    public bool Win { get; set; }

    public string ToDisplayString();   // e.g. "Ctrl+Shift+F6" or "F6"; use Keys enum names; "None" if KeyCode==0
    public bool IsEmpty => KeyCode == 0;
}
```

### WindowTargetInfo (Models.cs) — one background click target, JSON-serializable

```csharp
public sealed class WindowTargetInfo
{
    public long HwndValue { get; set; }            // window handle as long
    public string WindowTitle { get; set; } = "";
    public string ClassName { get; set; } = "";
    public int ClientX { get; set; }               // click point in target's CLIENT coords
    public int ClientY { get; set; }
    public bool Enabled { get; set; } = true;
    [System.Text.Json.Serialization.JsonIgnore]
    public IntPtr Hwnd => (IntPtr)HwndValue;
    public string ToDisplayString();               // "Title [Class] @ (x, y)" truncated sensibly
}
```

### MacroEvent / MacroScript (Models.cs) — JSON-serializable

```csharp
public sealed class MacroEvent
{
    public long TimeMs { get; set; }               // ms since recording start
    public MacroEventType Type { get; set; }
    public int X { get; set; }                     // screen coords (mouse events)
    public int Y { get; set; }
    public MouseButton Button { get; set; }        // mouse down/up only
    public int WheelDelta { get; set; }            // wheel only
    public ushort KeyCode { get; set; }            // key events only (VK)
}

public sealed class MacroScript
{
    public int Version { get; set; } = 1;
    public string Name { get; set; } = "";
    public List<MacroEvent> Events { get; set; } = new();
    public void Save(string path);                 // JSON, indented
    public static MacroScript Load(string path);   // throws on invalid
}
```

### AppSettings + SettingsStore (Models.cs)

```csharp
public sealed class AppSettings
{
    public ClickSettings Click { get; set; } = new();
    public HotkeyCombo ToggleClickerHotkey { get; set; } = new() { KeyCode = 0x75 };  // F6
    public HotkeyCombo ToggleRecordHotkey { get; set; } = new() { KeyCode = 0x76 };   // F7
    public HotkeyCombo TogglePlaybackHotkey { get; set; } = new() { KeyCode = 0x77 }; // F8
    public bool HoldClickMode { get; set; }
    public bool AlwaysOnTop { get; set; }
    public double PlaybackSpeed { get; set; } = 1.0;
    public int PlaybackRepeatCount { get; set; } = 1;      // 0 = infinite
    public List<WindowTargetInfo> BackgroundTargets { get; set; } = new();
}

public static class SettingsStore
{
    // %APPDATA%\OpenClick\settings.json
    public static AppSettings Load();   // returns new AppSettings() on missing/corrupt (swallow errors)
    public static void Save(AppSettings settings);  // create dir as needed, swallow IO errors
}
```

### IClickTarget + implementations (ClickTargets.cs)

```csharp
namespace OpenClick.Core;

public interface IClickTarget
{
    /// Perform one click action (double = two clicks). Called on engine thread.
    /// Returns false if the target is permanently gone (engine stops); true otherwise.
    bool PerformClick(ClickSettings settings, Random rng);
}

public sealed class ForegroundTarget : IClickTarget { public ForegroundTarget(); ... }
public sealed class WindowTarget : IClickTarget { public WindowTarget(WindowTargetInfo info); public WindowTargetInfo Info { get; } ... }
public sealed class MultiTarget : IClickTarget { public MultiTarget(IReadOnlyList<WindowTargetInfo> targets); ... }
```

### ClickEngine (ClickEngine.cs)

```csharp
public sealed class ClickEngine : IDisposable
{
    public bool IsRunning { get; }
    public long ClicksPerformed { get; }               // resets on Start
    public event Action? Started;                      // raised on engine/worker thread
    public event Action? Stopped;
    public event Action<long>? ClickTick;              // after each click action, arg = total clicks so far

    public void Start(ClickSettings settings, IClickTarget target); // no-op if running; clones settings
    public void Stop();                                             // blocks until thread exits (join w/ timeout 2s)
    public void Dispose();                                          // Stop
}
```

Loop behavior: `timeBeginPeriod(1)`; first click happens immediately on start, then wait effective interval between click actions. Wait = `Thread.Sleep` chunks of <=15ms checking a volatile stop flag (responsive stop; don't sleep the whole interval at once). Stop when `RepeatMode.Count` reaches `RepeatCount` click actions (a double-click action counts as one), when `PerformClick` returns false, or on `Stop()`. `timeEndPeriod(1)` + `Stopped` event on exit.

### HotkeyManager (HotkeyManager.cs)

```csharp
public sealed class HotkeyManager : IDisposable
{
    public HotkeyManager();                       // installs WH_KEYBOARD_LL (call from UI thread)
    public void Register(string actionId, HotkeyCombo combo);  // replaces existing binding for actionId
    public void Unregister(string actionId);
    public bool Suspended { get; set; }           // true while capturing a new hotkey in UI
    public event Action<string>? HotkeyPressed;   // actionId; raised from hook callback
    public void Dispose();                        // unhook
}
```

Match on key-down of `KeyCode` when the modifier states (Ctrl/Shift/Alt/Win via `GetAsyncKeyState` high bit on VK_CONTROL/VK_SHIFT/VK_MENU/VK_LWIN|VK_RWIN) equal the combo exactly (a combo without Ctrl must NOT fire while Ctrl is down). Ignore injected events (`LLKHF_INJECTED`). Do not swallow the keystroke. Ignore key auto-repeat (fire once per physical press: track last key state or use previous-state, simplest: suppress if same combo fired < 250ms ago while key held — acceptable: fire only on transition by tracking whether main key was already down).

### HoldClickMonitor (HoldClickMonitor.cs)

```csharp
public sealed class HoldClickMonitor : IDisposable
{
    public HoldClickMonitor();                    // installs WH_MOUSE_LL (UI thread)
    public bool Enabled { get; set; }             // default false
    public MouseButton TriggerButton { get; set; } = MouseButton.Left;
    public event Action? HoldStarted;             // physical (non-injected) trigger down
    public event Action? HoldEnded;               // physical trigger up
    public void Dispose();
}
```

Must ignore events with `LLMHF_INJECTED` set (so the engine's own SendInput clicks never trigger it — this prevents feedback loops when trigger button == clicked button). Only raise when `Enabled`. Raise HoldEnded on disable if currently held.

### WindowPicker (WindowPicker.cs + UI/WindowPickerOverlay.cs)

```csharp
public sealed class WindowPicker
{
    /// Modal-ish pick session: shows overlay/highlight; user hovers, left-click selects, Esc cancels.
    /// Returns null on cancel. Must exclude OpenClick's own windows from hit-testing.
    public static WindowTargetInfo? PickTarget(IWin32Window owner);
}
```

Implementation: LL mouse hook during the session swallows the selecting left-click. A click-through highlight form (`WS_EX_LAYERED|WS_EX_TRANSPARENT|WS_EX_TOOLWINDOW`, TopMost, semi-transparent border/fill) tracks the hovered window on a ~30ms UI timer using `WindowFromPoint`; resolve the deepest visible child under the cursor (walk with `RealChildWindowFromPoint` from the top-level, mapping the point to client coords each level). A small floating info label shows `Title [Class] (x,y)`. On select: compute the point in the chosen window's client coords (`ScreenToClient`), fill `WindowTargetInfo`. Runs a nested message loop (`Application.DoEvents` loop or modal hidden form) until done.

### Recorder (Recorder.cs)

```csharp
public sealed class Recorder : IDisposable
{
    public Recorder();                            // hooks installed only while recording
    public bool IsRecording { get; }
    public IReadOnlyList<ushort> IgnoreKeys { get; set; }  // VKs to skip (the toggle hotkey keys)
    public void Start();                          // clears buffer, installs WH_MOUSE_LL + WH_KEYBOARD_LL (UI thread)
    public MacroScript StopAndGet();              // unhooks, returns recorded script
    public event Action<int>? EventCaptured;      // arg = event count so far
    public void Dispose();
}
```

Record physical events only (skip injected). Mouse moves throttled: keep a move only if >=10ms since the last kept move (always keep moves immediately preceding a down/up — simplest: record the current cursor position as a MouseMove right before each down/up if the last kept move is older than 10ms). Timestamps via `Stopwatch` from `Start()`. Skip key events whose VK is in `IgnoreKeys`, and skip modifier keys currently held as part of the toggle combo at start (covered by IgnoreKeys — MainForm passes main key + modifier VKs).

### Player (Player.cs)

```csharp
public sealed class Player : IDisposable
{
    public bool IsPlaying { get; }
    public event Action? PlaybackStarted;
    public event Action? PlaybackFinished;        // raised when loop ends (all repeats done or stopped)
    public event Action<int>? RepeatCompleted;    // arg = completed repeat number (1-based)

    public void Start(MacroScript script, double speed, int repeatCount); // repeatCount 0 = infinite; no-op if playing
    public void Stop();                           // blocks (join w/ timeout 2s)
    public void Dispose();
}
```

Playback thread: `timeBeginPeriod(1)`. For each event, target wall-time = `TimeMs / speed`; sleep in <=15ms chunks (volatile stop flag) until due, then emit via `SendInput`:
- MouseMove / MouseDown / MouseUp / MouseWheel: move cursor using `MOUSEEVENTF_MOVE|MOUSEEVENTF_ABSOLUTE|MOUSEEVENTF_VIRTUALDESK` with coords normalized to the virtual screen: `nx = (x - SM_XVIRTUALSCREEN) * 65535 / (SM_CXVIRTUALSCREEN - 1)` (same for y). For down/up also send the button flag (send move+button as two INPUTs in one SendInput call). Wheel: `MOUSEEVENTF_WHEEL` with `mouseData = WheelDelta`.
- KeyDown/KeyUp: `KEYBDINPUT` with `wVk = KeyCode` (+ `KEYEVENTF_KEYUP`), also set `wScan = MapVirtualKey(vk, 0)`.
Between repeats: 250ms pause. `timeEndPeriod(1)` on exit.

---

## NativeMethods.cs — required surface (exact)

`namespace OpenClick.Native;` — `public static class NativeMethods` + public structs. All signatures standard; include:

**Structs:** `INPUT` (with `type` + union via `[StructLayout(LayoutKind.Explicit)]` `InputUnion` containing `MOUSEINPUT`, `KEYBDINPUT`, `HARDWAREINPUT`), `MOUSEINPUT`, `KEYBDINPUT`, `HARDWAREINPUT`, `POINT`, `RECT`, `MSLLHOOKSTRUCT`, `KBDLLHOOKSTRUCT`.

**Delegates:** `public delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);`

**Functions (user32 unless noted):**
`SendInput`, `GetCursorPos`, `SetCursorPos`, `SetWindowsHookEx` (string module overload using `GetModuleHandle(null)` from kernel32), `UnhookWindowsHookEx`, `CallNextHookEx`, `GetAsyncKeyState`, `MapVirtualKey`, `PostMessage`, `SendMessageTimeout`, `WindowFromPoint`, `RealChildWindowFromPoint`, `ChildWindowFromPointEx`, `ScreenToClient`, `ClientToScreen`, `GetWindowRect`, `GetClientRect`, `GetWindowText`+`GetWindowTextLength`, `GetClassName`, `GetWindowThreadProcessId`, `IsWindow`, `IsWindowVisible`, `GetAncestor`, `GetSystemMetrics`, `GetDoubleClickTime`, `SetForegroundWindow`; kernel32: `GetModuleHandle`, `GetCurrentThreadId`; winmm: `timeBeginPeriod`, `timeEndPeriod`.

**Constants (public const int unless noted):**
- Hooks: `WH_KEYBOARD_LL = 13`, `WH_MOUSE_LL = 14`, `HC_ACTION = 0`
- LL flags: `LLKHF_INJECTED = 0x10`, `LLMHF_INJECTED = 0x1`
- Wm: `WM_KEYDOWN=0x100, WM_KEYUP=0x101, WM_SYSKEYDOWN=0x104, WM_SYSKEYUP=0x105, WM_MOUSEMOVE=0x200, WM_LBUTTONDOWN=0x201, WM_LBUTTONUP=0x202, WM_LBUTTONDBLCLK=0x203, WM_RBUTTONDOWN=0x204, WM_RBUTTONUP=0x205, WM_RBUTTONDBLCLK=0x206, WM_MBUTTONDOWN=0x207, WM_MBUTTONUP=0x208, WM_MBUTTONDBLCLK=0x209, WM_MOUSEWHEEL=0x20A, WM_XBUTTONDOWN=0x20B, WM_XBUTTONUP=0x20C`
- `MK_LBUTTON=0x1, MK_RBUTTON=0x2, MK_MBUTTON=0x10`
- Input: `INPUT_MOUSE=0, INPUT_KEYBOARD=1`; `MOUSEEVENTF_MOVE=0x1, MOUSEEVENTF_LEFTDOWN=0x2, MOUSEEVENTF_LEFTUP=0x4, MOUSEEVENTF_RIGHTDOWN=0x8, MOUSEEVENTF_RIGHTUP=0x10, MOUSEEVENTF_MIDDLEDOWN=0x20, MOUSEEVENTF_MIDDLEUP=0x40, MOUSEEVENTF_WHEEL=0x800, MOUSEEVENTF_VIRTUALDESK=0x4000, MOUSEEVENTF_ABSOLUTE=0x8000`; `KEYEVENTF_KEYUP=0x2`
- Keys: `VK_CONTROL=0x11, VK_MENU=0x12, VK_SHIFT=0x10, VK_LWIN=0x5B, VK_RWIN=0x5C, VK_ESCAPE=0x1B`
- Metrics: `SM_XVIRTUALSCREEN=76, SM_YVIRTUALSCREEN=77, SM_CXVIRTUALSCREEN=78, SM_CYVIRTUALSCREEN=79`
- `GA_ROOT = 2`; `CWP_SKIPINVISIBLE=0x1, CWP_SKIPTRANSPARENT=0x4`; `SMTO_ABORTIFHUNG=0x2`

**Helpers (in NativeMethods):**
```csharp
public static IntPtr MakeLParam(int loWord, int hiWord); // (hiWord << 16) | (loWord & 0xFFFF)
public static void SendMouseButton(MouseButtonFlagsDown down, ...) // NOT needed; keep raw — helpers below instead:
public static void SendMouseClick(uint downFlag, uint upFlag);                 // two INPUTs, one SendInput call
public static string GetWindowTitle(IntPtr hWnd);
public static string GetWindowClass(IntPtr hWnd);
```

## ClickTargets.cs behavior details

- `ForegroundTarget`: if `PositionMode.Fixed`, `SetCursorPos(FixedX, FixedY)` first. Send down+up via `SendInput` (flags per `settings.Button`). Double = down/up, sleep `Math.Clamp(settings.TotalIntervalMs / 4, 10, 50)` ms, down/up. Always returns true.
- `WindowTarget`: if `!IsWindow(hwnd)` return false. lParam = `MakeLParam(ClientX, ClientY)`; wParam for down = `MK_LBUTTON`/`MK_RBUTTON`/`MK_MBUTTON`; post `WM_MOUSEMOVE`(wParam 0) then `WM_xBUTTONDOWN` then `WM_xBUTTONUP`(wParam 0). Double: down,up,dblclk,up. Uses `PostMessage` only (never activate the window).
- `MultiTarget`: iterate enabled targets; post to each (skip dead ones; only return false when ALL targets are dead).

---

## UI spec (MainForm + controls)

Follow standard WinForms look; clean, compact, resizable-off (FixedSingle), ~540x460. `Segoe UI 9pt`. App icon optional. StatusStrip at bottom: state label ("Idle" / "Clicking… (n)" / "Recording… (n events)" / "Playing…") + hotkey reminder label.

**Tab 1 — Autoclick**
- GroupBox "Click interval": 4 NumericUpDown (Hours 0-23, Mins 0-59, Secs 0-59, Ms 0-9999) + CheckBox "Random offset ±" + NumericUpDown ms (0-60000).
- GroupBox "Click options": ComboBox Mouse button (Left/Right/Middle), ComboBox Click type (Single/Double), CheckBox "Hold mode — autoclick while the trigger mouse button is physically held".
- GroupBox "Click repeat": RadioButton "Repeat until stopped", RadioButton "Repeat" + NumericUpDown (1-1000000) "times".
- GroupBox "Cursor position": RadioButton "Current position", RadioButton "Fixed" + X/Y NumericUpDown (-32768..32767) + Button "Pick location" (opens PickLocationOverlay; fills X/Y; selects Fixed).
- Big Start button + Stop button (only one enabled at a time), each showing hotkey suffix, e.g. "Start (F6)".

**Tab 2 — Record & Playback**
- Buttons: "● Record (F7)" toggle, event-count label.
- Playback group: speed ComboBox (0.25x/0.5x/1x/2x/4x — editable numeric ok), repeat NumericUpDown (0=∞, label it), "▶ Play (F8)" toggle, progress label.
- "Save macro…" / "Load macro…" buttons (JSON, `*.ocmacro.json` filter, default dir Documents).

**Tab 3 — Background & Multiclick**
- ListView (Details: Window, Class, Point, Enabled checkbox via CheckBoxes=true) of `WindowTargetInfo`.
- Buttons: "Add target… (pick)", "Remove", "Test click" (click selected target once).
- Label note: "Background clicks use PostMessage; some apps (games using raw input) ignore them."
- "Start background clicking" / Stop — uses Tab 1's interval/button/type/repeat settings, targets from list.

**Tab 4 — Settings**
- Three HotkeyCaptureBox rows: Toggle clicker / Toggle recording / Toggle playback. (Click box, press combo, Esc clears; sets `HotkeyManager.Suspended` while capturing.)
- CheckBox "Always on top".
- Link label to GitHub repo.

**Wiring rules**
- One shared `ClickEngine`. Hotkey "toggle-clicker": if engine running → Stop; else start with ForegroundTarget (Tab 1 settings). Background Start button uses `MultiTarget`. Starting one while the other runs: Stop first, then start new.
- Hold mode: when checkbox on, HoldClickMonitor.Enabled = true with TriggerButton = selected click button; HoldStarted → engine.Start (foreground), HoldEnded → engine.Stop. Start/Stop buttons disabled while hold mode is on (label explains).
- Record toggle: Recorder.Start / StopAndGet → keep script in memory, update count label. Pass toggle-hotkey VKs (main key + used modifier VKs) as IgnoreKeys.
- Playback toggle: Player.Start(currentScript, speed, repeat) / Stop. Also stop recording before playing; stop playback before recording; never both.
- All engine/player events marshaled with `BeginInvoke` before touching controls.
- Load `AppSettings` on start, apply to controls; save on FormClosing.
- On close: dispose engine, player, recorder, hooks.

**PickLocationOverlay**: borderless TopMost form covering `SystemInformation.VirtualScreen`, 25% opacity black, `Cursor = Cursors.Cross`, floating label near cursor with "X, Y — click to select, Esc to cancel". Returns `Point?` (screen coords) via `static Point? Pick()` showing itself as dialog.

**HotkeyCaptureBox**: subclass of `TextBox`, ReadOnly, on focus shows "Press keys…"; override `ProcessCmdKey`/KeyDown to capture modifiers+key; ignores pure-modifier presses until a non-modifier key arrives; Esc = clear to None; raises `event Action<HotkeyCombo>? ComboChanged`; property `HotkeyCombo Combo { get; set; }` updates display text.

---

## Program.cs

`ApplicationConfiguration.Initialize(); Application.Run(new UI.MainForm());` with `[STAThread]`.

## Acceptance criteria (QA checklist)

1. Start/stop clicking via button and via customizable hotkey (incl. combos like Ctrl+Shift+F6) while another app is focused.
2. Interval math correct across h/m/s/ms; random offset varies intervals; 10ms interval sustains >50 cps.
3. Left/right/middle, single/double clicks land; repeat count stops exactly at N; fixed-position mode clicks at picked point.
4. Hold mode: hold trigger → rapid clicks, release → stops; no feedback loop.
5. Record mouse+keyboard, replay at 0.5x/1x/2x with faithful positions/timing; repeat count & infinite; hotkeys don't leak into recording.
6. Background target picked with highlight overlay; clicks land in unfocused window (test on Notepad/Paint); multi-target clicks all enabled rows each tick.
7. Settings and hotkeys persist across restart. No crashes on window-gone targets, empty macro play, hotkey conflicts.
