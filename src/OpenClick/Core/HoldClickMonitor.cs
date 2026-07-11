using OpenClick.Native;

namespace OpenClick.Core;

/// <summary>
/// Hold-to-click support: raises HoldStarted/HoldEnded around the physical (non-injected)
/// down/up of a configurable trigger mouse button, via a WH_MOUSE_LL low-level hook.
/// Must be constructed on the UI thread.
/// </summary>
public sealed class HoldClickMonitor : IDisposable
{
    // Keep the delegate alive in a field so the GC never collects it while the hook is installed.
    private readonly LowLevelProc _proc;
    private readonly IntPtr _hookHandle;
    private bool _disposed;
    private bool _holding;
    private bool _enabled;
    private MouseButton _triggerButton = MouseButton.Left;

    // The trigger button captured when the current hold started, so a mid-hold
    // change of TriggerButton can never orphan the hold (button-up still matches).
    private MouseButton _latchedTriggerButton;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
            {
                return;
            }

            _enabled = value;
            if (!_enabled && _holding)
            {
                _holding = false;
                RaiseHoldEnded();
            }
        }
    }

    public MouseButton TriggerButton
    {
        get => _triggerButton;
        set
        {
            if (_triggerButton == value)
            {
                return;
            }

            _triggerButton = value;
            if (_holding)
            {
                _holding = false;
                RaiseHoldEnded();
            }
        }
    }

    public event Action? HoldStarted;
    public event Action? HoldEnded;

    public HoldClickMonitor()
    {
        _proc = HookCallback;
        IntPtr moduleHandle = NativeMethods.GetModuleHandle(null);
        _hookHandle = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _proc, moduleHandle, 0);
        if (_hookHandle == IntPtr.Zero)
        {
            throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && Enabled)
        {
            var info = System.Runtime.InteropServices.Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            if ((info.flags & NativeMethods.LLMHF_INJECTED) == 0)
            {
                int msg = (int)wParam;
                (int downMsg, _) = GetTriggerMessages(_triggerButton);
                (_, int upMsg) = GetTriggerMessages(_latchedTriggerButton);

                if (msg == downMsg)
                {
                    if (!_holding)
                    {
                        _holding = true;
                        _latchedTriggerButton = _triggerButton;
                        try
                        {
                            HoldStarted?.Invoke();
                        }
                        catch
                        {
                            // Never let a subscriber exception propagate across the native hook boundary.
                        }
                    }
                }
                else if (msg == upMsg)
                {
                    if (_holding)
                    {
                        _holding = false;
                        RaiseHoldEnded();
                    }
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private static (int downMsg, int upMsg) GetTriggerMessages(MouseButton button) => button switch
    {
        MouseButton.Right => (NativeMethods.WM_RBUTTONDOWN, NativeMethods.WM_RBUTTONUP),
        MouseButton.Middle => (NativeMethods.WM_MBUTTONDOWN, NativeMethods.WM_MBUTTONUP),
        _ => (NativeMethods.WM_LBUTTONDOWN, NativeMethods.WM_LBUTTONUP),
    };

    private void RaiseHoldEnded()
    {
        try
        {
            HoldEnded?.Invoke();
        }
        catch
        {
            // Never let a subscriber exception propagate across the native hook boundary.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_holding)
        {
            _holding = false;
            RaiseHoldEnded();
        }

        if (_hookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookHandle);
        }
    }
}
