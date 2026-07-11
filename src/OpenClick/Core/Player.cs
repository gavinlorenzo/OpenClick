using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using OpenClick.Native;

namespace OpenClick.Core;

/// <summary>Plays back a recorded <see cref="MacroScript"/> via SendInput on a dedicated background thread.</summary>
public sealed class Player : IDisposable
{
    private readonly object _lock = new();
    private Thread? _thread;
    private volatile bool _stopRequested;
    private volatile bool _isPlaying;

    public bool IsPlaying => _isPlaying;

    public event Action? PlaybackStarted;
    public event Action? PlaybackFinished;
    public event Action<int>? RepeatCompleted;

    /// <summary>Starts playback. No-op if already playing or the script has no events. repeatCount 0 = infinite.</summary>
    public void Start(MacroScript script, double speed, int repeatCount)
    {
        lock (_lock)
        {
            if (_isPlaying || script.Events.Count == 0)
            {
                return;
            }

            double clampedSpeed = Math.Clamp(speed, 0.05, 20.0);
            _stopRequested = false;
            _isPlaying = true;

            _thread = new Thread(() => PlaybackLoop(script, clampedSpeed, repeatCount))
            {
                IsBackground = true,
                Name = "OpenClick.Player",
            };
            _thread.Start();
        }
    }

    /// <summary>Signals the playback thread to stop and waits (with timeout) for it to exit. Safe when not playing.</summary>
    public void Stop()
    {
        Thread? threadToJoin;
        lock (_lock)
        {
            if (!_isPlaying)
            {
                return;
            }

            _stopRequested = true;
            threadToJoin = _thread;
        }

        if (threadToJoin != null && Thread.CurrentThread != threadToJoin)
        {
            threadToJoin.Join(2000);
        }
    }

    private void PlaybackLoop(MacroScript script, double speed, int repeatCount)
    {
        NativeMethods.timeBeginPeriod(1);
        try
        {
            PlaybackStarted?.Invoke();

            bool infinite = repeatCount == 0;
            int repeatsDone = 0;

            while (infinite || repeatsDone < repeatCount)
            {
                var sw = Stopwatch.StartNew();

                foreach (var evt in script.Events)
                {
                    double due = evt.TimeMs / speed;

                    while (sw.ElapsedMilliseconds < due)
                    {
                        if (_stopRequested)
                        {
                            return;
                        }

                        double remaining = due - sw.ElapsedMilliseconds;
                        int sleepMs = (int)Math.Max(0, Math.Min(15, remaining));
                        Thread.Sleep(sleepMs);
                    }

                    if (_stopRequested)
                    {
                        return;
                    }

                    Emit(evt);
                }

                repeatsDone++;
                RepeatCompleted?.Invoke(repeatsDone);

                if (infinite || repeatsDone < repeatCount)
                {
                    if (!SleepChunked(250))
                    {
                        return;
                    }
                }
            }
        }
        finally
        {
            NativeMethods.timeEndPeriod(1);
            _isPlaying = false;
            PlaybackFinished?.Invoke();
        }
    }

    /// <summary>Sleeps up to totalMs in &lt;=15ms chunks, checking the stop flag between chunks. Returns false if stopped early.</summary>
    private bool SleepChunked(int totalMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < totalMs)
        {
            if (_stopRequested)
            {
                return false;
            }

            int remaining = (int)(totalMs - sw.ElapsedMilliseconds);
            Thread.Sleep(Math.Max(0, Math.Min(15, remaining)));
        }

        return !_stopRequested;
    }

    private static void Emit(MacroEvent evt)
    {
        switch (evt.Type)
        {
            case MacroEventType.MouseMove:
                SendMouseMove(evt.X, evt.Y);
                break;

            case MacroEventType.MouseDown:
            case MacroEventType.MouseUp:
                SendMouseButton(evt);
                break;

            case MacroEventType.MouseWheel:
                SendMouseWheel(evt);
                break;

            case MacroEventType.KeyDown:
            case MacroEventType.KeyUp:
                SendKey(evt);
                break;
        }
    }

    private static (int nx, int ny) NormalizeToVirtualDesktop(int x, int y)
    {
        int vx = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        int vy = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        int vw = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        int vh = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);

        int nx = (int)((long)(x - vx) * 65535 / Math.Max(1, vw - 1));
        int ny = (int)((long)(y - vy) * 65535 / Math.Max(1, vh - 1));
        return (nx, ny);
    }

    private static uint MoveFlags => (uint)(NativeMethods.MOUSEEVENTF_MOVE
        | NativeMethods.MOUSEEVENTF_ABSOLUTE
        | NativeMethods.MOUSEEVENTF_VIRTUALDESK);

    private static void SendMouseMove(int x, int y)
    {
        var (nx, ny) = NormalizeToVirtualDesktop(x, y);

        var inputs = new INPUT[1];
        inputs[0].type = NativeMethods.INPUT_MOUSE;
        inputs[0].U.mi.dx = nx;
        inputs[0].U.mi.dy = ny;
        inputs[0].U.mi.dwFlags = MoveFlags;

        NativeMethods.SendInput(1, inputs, Marshal.SizeOf<INPUT>());
    }

    private static void SendMouseButton(MacroEvent evt)
    {
        var (nx, ny) = NormalizeToVirtualDesktop(evt.X, evt.Y);
        uint buttonFlag = GetButtonFlag(evt.Button, evt.Type);

        var inputs = new INPUT[2];
        inputs[0].type = NativeMethods.INPUT_MOUSE;
        inputs[0].U.mi.dx = nx;
        inputs[0].U.mi.dy = ny;
        inputs[0].U.mi.dwFlags = MoveFlags;

        inputs[1].type = NativeMethods.INPUT_MOUSE;
        inputs[1].U.mi.dwFlags = buttonFlag;

        NativeMethods.SendInput(2, inputs, Marshal.SizeOf<INPUT>());
    }

    private static void SendMouseWheel(MacroEvent evt)
    {
        var (nx, ny) = NormalizeToVirtualDesktop(evt.X, evt.Y);

        var inputs = new INPUT[2];
        inputs[0].type = NativeMethods.INPUT_MOUSE;
        inputs[0].U.mi.dx = nx;
        inputs[0].U.mi.dy = ny;
        inputs[0].U.mi.dwFlags = MoveFlags;

        inputs[1].type = NativeMethods.INPUT_MOUSE;
        inputs[1].U.mi.mouseData = unchecked((uint)evt.WheelDelta);
        inputs[1].U.mi.dwFlags = (uint)NativeMethods.MOUSEEVENTF_WHEEL;

        NativeMethods.SendInput(2, inputs, Marshal.SizeOf<INPUT>());
    }

    private static uint GetButtonFlag(MouseButton button, MacroEventType type)
    {
        bool down = type == MacroEventType.MouseDown;
        return button switch
        {
            MouseButton.Left => (uint)(down ? NativeMethods.MOUSEEVENTF_LEFTDOWN : NativeMethods.MOUSEEVENTF_LEFTUP),
            MouseButton.Right => (uint)(down ? NativeMethods.MOUSEEVENTF_RIGHTDOWN : NativeMethods.MOUSEEVENTF_RIGHTUP),
            MouseButton.Middle => (uint)(down ? NativeMethods.MOUSEEVENTF_MIDDLEDOWN : NativeMethods.MOUSEEVENTF_MIDDLEUP),
            _ => 0u,
        };
    }

    private static void SendKey(MacroEvent evt)
    {
        var inputs = new INPUT[1];
        inputs[0].type = NativeMethods.INPUT_KEYBOARD;
        inputs[0].U.ki.wVk = evt.KeyCode;
        inputs[0].U.ki.wScan = (ushort)NativeMethods.MapVirtualKey(evt.KeyCode, 0);
        inputs[0].U.ki.dwFlags = evt.Type == MacroEventType.KeyUp ? (uint)NativeMethods.KEYEVENTF_KEYUP : 0u;

        NativeMethods.SendInput(1, inputs, Marshal.SizeOf<INPUT>());
    }

    public void Dispose()
    {
        Stop();
    }
}
