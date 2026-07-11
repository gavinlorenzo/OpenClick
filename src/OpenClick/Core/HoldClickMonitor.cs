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
                HoldEnded?.Invoke();
            }
        }
    }

    public MouseButton TriggerButton { get; set; } = MouseButton.Left;

    public event Action? HoldStarted;
    public event Action? HoldEnded;

    public HoldClickMonitor()
    {
        _proc = HookCallback;
        IntPtr moduleHandle = NativeMethods.GetModuleHandle(null);
        _hookHandle = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _proc, moduleHandle, 0);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && Enabled)
        {
            var info = System.Runtime.InteropServices.Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            if ((info.flags & NativeMethods.LLMHF_INJECTED) == 0)
            {
                int msg = (int)wParam;
                (int downMsg, int upMsg) = GetTriggerMessages();

                if (msg == downMsg)
                {
                    if (!_holding)
                    {
                        _holding = true;
                        HoldStarted?.Invoke();
                    }
                }
                else if (msg == upMsg)
                {
                    if (_holding)
                    {
                        _holding = false;
                        HoldEnded?.Invoke();
                    }
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private (int downMsg, int upMsg) GetTriggerMessages() => TriggerButton switch
    {
        MouseButton.Right => (NativeMethods.WM_RBUTTONDOWN, NativeMethods.WM_RBUTTONUP),
        MouseButton.Middle => (NativeMethods.WM_MBUTTONDOWN, NativeMethods.WM_MBUTTONUP),
        _ => (NativeMethods.WM_LBUTTONDOWN, NativeMethods.WM_LBUTTONUP),
    };

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_hookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookHandle);
        }
    }
}
