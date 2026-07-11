using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using OpenClick.Native;

namespace OpenClick.Core;

/// <summary>Records physical mouse + keyboard input via WH_MOUSE_LL / WH_KEYBOARD_LL while active.</summary>
public sealed class Recorder : IDisposable
{
    private readonly object _lock = new();
    private readonly List<MacroEvent> _events = new();
    private readonly Stopwatch _stopwatch = new();

    // Keep delegate instances alive in fields so the GC never collects them while hooked.
    private LowLevelProc? _mouseProc;
    private LowLevelProc? _keyboardProc;
    private IntPtr _mouseHook = IntPtr.Zero;
    private IntPtr _keyboardHook = IntPtr.Zero;

    private long? _lastKeptMoveTimeMs;
    private bool _disposed;

    public bool IsRecording { get; private set; }

    /// <summary>VKs to skip when recording key events (e.g. the toggle-record hotkey combo).</summary>
    public IReadOnlyList<ushort> IgnoreKeys { get; set; } = Array.Empty<ushort>();

    public event Action<int>? EventCaptured;

    /// <summary>Clears the buffer and installs the low-level hooks. Call from the UI thread.</summary>
    public void Start()
    {
        if (IsRecording)
        {
            return;
        }

        lock (_lock)
        {
            _events.Clear();
        }

        _lastKeptMoveTimeMs = null;

        _mouseProc = MouseHookCallback;
        _keyboardProc = KeyboardHookCallback;

        IntPtr moduleHandle = NativeMethods.GetModuleHandle(null);
        _mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _mouseProc, moduleHandle, 0);
        _keyboardHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _keyboardProc, moduleHandle, 0);

        _stopwatch.Restart();
        IsRecording = true;
    }

    /// <summary>Uninstalls the hooks and returns the recorded script.</summary>
    public MacroScript StopAndGet()
    {
        Unhook();
        _stopwatch.Stop();
        IsRecording = false;

        lock (_lock)
        {
            return new MacroScript { Events = new List<MacroEvent>(_events) };
        }
    }

    private void Unhook()
    {
        if (_mouseHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }

        if (_keyboardHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }

        _mouseProc = null;
        _keyboardProc = null;
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode == NativeMethods.HC_ACTION)
        {
            var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            if ((hookStruct.flags & NativeMethods.LLMHF_INJECTED) == 0)
            {
                long timeMs = _stopwatch.ElapsedMilliseconds;
                int x = hookStruct.pt.X;
                int y = hookStruct.pt.Y;
                int msg = wParam.ToInt32();

                switch (msg)
                {
                    case NativeMethods.WM_MOUSEMOVE:
                        MaybeAppendMove(x, y, timeMs);
                        break;

                    case NativeMethods.WM_LBUTTONDOWN:
                        AppendButton(x, y, timeMs, MacroEventType.MouseDown, MouseButton.Left);
                        break;
                    case NativeMethods.WM_LBUTTONUP:
                        AppendButton(x, y, timeMs, MacroEventType.MouseUp, MouseButton.Left);
                        break;

                    case NativeMethods.WM_RBUTTONDOWN:
                        AppendButton(x, y, timeMs, MacroEventType.MouseDown, MouseButton.Right);
                        break;
                    case NativeMethods.WM_RBUTTONUP:
                        AppendButton(x, y, timeMs, MacroEventType.MouseUp, MouseButton.Right);
                        break;

                    case NativeMethods.WM_MBUTTONDOWN:
                        AppendButton(x, y, timeMs, MacroEventType.MouseDown, MouseButton.Middle);
                        break;
                    case NativeMethods.WM_MBUTTONUP:
                        AppendButton(x, y, timeMs, MacroEventType.MouseUp, MouseButton.Middle);
                        break;

                    case NativeMethods.WM_MOUSEWHEEL:
                        Append(new MacroEvent
                        {
                            TimeMs = timeMs,
                            Type = MacroEventType.MouseWheel,
                            X = x,
                            Y = y,
                            WheelDelta = (short)(hookStruct.mouseData >> 16),
                        });
                        break;
                }
            }
        }

        return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode == NativeMethods.HC_ACTION)
        {
            var hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if ((hookStruct.flags & NativeMethods.LLKHF_INJECTED) == 0)
            {
                int msg = wParam.ToInt32();
                bool isDown = msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN;
                bool isUp = msg == NativeMethods.WM_KEYUP || msg == NativeMethods.WM_SYSKEYUP;

                if (isDown || isUp)
                {
                    ushort vk = (ushort)hookStruct.vkCode;
                    if (!IgnoreKeys.Contains(vk))
                    {
                        Append(new MacroEvent
                        {
                            TimeMs = _stopwatch.ElapsedMilliseconds,
                            Type = isDown ? MacroEventType.KeyDown : MacroEventType.KeyUp,
                            KeyCode = vk,
                        });
                    }
                }
            }
        }

        return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    /// <summary>
    /// Appends a mouse down/up event, first inserting a MouseMove at the same point/time
    /// if the last kept move is older than 10ms (or none has been kept yet).
    /// </summary>
    private void AppendButton(int x, int y, long timeMs, MacroEventType type, MouseButton button)
    {
        MaybeAppendMove(x, y, timeMs);
        Append(new MacroEvent
        {
            TimeMs = timeMs,
            Type = type,
            X = x,
            Y = y,
            Button = button,
        });
    }

    private void MaybeAppendMove(int x, int y, long timeMs)
    {
        if (_lastKeptMoveTimeMs is null || timeMs - _lastKeptMoveTimeMs.Value >= 10)
        {
            Append(new MacroEvent
            {
                TimeMs = timeMs,
                Type = MacroEventType.MouseMove,
                X = x,
                Y = y,
            });
            _lastKeptMoveTimeMs = timeMs;
        }
    }

    private void Append(MacroEvent evt)
    {
        int count;
        lock (_lock)
        {
            _events.Add(evt);
            count = _events.Count;
        }

        EventCaptured?.Invoke(count);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (IsRecording)
        {
            Unhook();
            _stopwatch.Stop();
            IsRecording = false;
        }
    }
}
