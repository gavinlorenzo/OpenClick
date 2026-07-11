using OpenClick.Native;

namespace OpenClick.Core;

/// <summary>
/// Global hotkey combo dispatcher backed by a WH_KEYBOARD_LL low-level keyboard hook.
/// Must be constructed on the UI thread (the thread that owns the message pump).
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private readonly object _lock = new();
    private readonly Dictionary<string, HotkeyCombo> _combos = new();

    // Keep the delegate alive in a field so the GC never collects it while the
    // hook is installed (a collected delegate would crash the process on callback).
    private readonly LowLevelProc _proc;
    private readonly IntPtr _hookHandle;

    // Tracks the vk of the physically-held main key so we fire only once per
    // physical press (no refire until the matching key-up is observed).
    private int _downVkCode;
    private bool _disposed;

    public bool Suspended { get; set; }

    public event Action<string>? HotkeyPressed;

    public HotkeyManager()
    {
        _proc = HookCallback;
        IntPtr moduleHandle = NativeMethods.GetModuleHandle(null);
        _hookHandle = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _proc, moduleHandle, 0);
    }

    public void Register(string actionId, HotkeyCombo combo)
    {
        if (combo.IsEmpty)
        {
            return;
        }

        lock (_lock)
        {
            _combos[actionId] = combo;
        }
    }

    public void Unregister(string actionId)
    {
        lock (_lock)
        {
            _combos.Remove(actionId);
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && !Suspended)
        {
            int msg = (int)wParam;
            if (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN)
            {
                HandleKeyDown(lParam);
            }
            else if (msg == NativeMethods.WM_KEYUP || msg == NativeMethods.WM_SYSKEYUP)
            {
                HandleKeyUp(lParam);
            }
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private void HandleKeyDown(IntPtr lParam)
    {
        var info = System.Runtime.InteropServices.Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

        if ((info.flags & NativeMethods.LLKHF_INJECTED) != 0)
        {
            return;
        }

        int vkCode = (int)info.vkCode;

        if (IsPureModifierKey(vkCode))
        {
            return;
        }

        // Fire once per physical press: ignore auto-repeat while the same key is down.
        if (_downVkCode == vkCode)
        {
            return;
        }

        _downVkCode = vkCode;

        bool ctrl = IsKeyDown(NativeMethods.VK_CONTROL);
        bool shift = IsKeyDown(NativeMethods.VK_SHIFT);
        bool alt = IsKeyDown(NativeMethods.VK_MENU);
        bool win = IsKeyDown(NativeMethods.VK_LWIN) || IsKeyDown(NativeMethods.VK_RWIN);

        string? matchedActionId = null;
        lock (_lock)
        {
            foreach (KeyValuePair<string, HotkeyCombo> kvp in _combos)
            {
                HotkeyCombo combo = kvp.Value;
                if (combo.KeyCode == vkCode &&
                    combo.Ctrl == ctrl &&
                    combo.Shift == shift &&
                    combo.Alt == alt &&
                    combo.Win == win)
                {
                    matchedActionId = kvp.Key;
                    break;
                }
            }
        }

        if (matchedActionId != null)
        {
            HotkeyPressed?.Invoke(matchedActionId);
        }
    }

    private void HandleKeyUp(IntPtr lParam)
    {
        var info = System.Runtime.InteropServices.Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

        if ((info.flags & NativeMethods.LLKHF_INJECTED) != 0)
        {
            return;
        }

        int vkCode = (int)info.vkCode;
        if (_downVkCode == vkCode)
        {
            _downVkCode = 0;
        }
    }

    private static bool IsPureModifierKey(int vkCode) =>
        vkCode == NativeMethods.VK_CONTROL ||
        vkCode == NativeMethods.VK_SHIFT ||
        vkCode == NativeMethods.VK_MENU ||
        vkCode == NativeMethods.VK_LWIN ||
        vkCode == NativeMethods.VK_RWIN ||
        vkCode == 0xA0 || // VK_LSHIFT
        vkCode == 0xA1 || // VK_RSHIFT
        vkCode == 0xA2 || // VK_LCONTROL
        vkCode == 0xA3 || // VK_RCONTROL
        vkCode == 0xA4 || // VK_LMENU
        vkCode == 0xA5;   // VK_RMENU

    private static bool IsKeyDown(int vkCode) =>
        (NativeMethods.GetAsyncKeyState(vkCode) & 0x8000) != 0;

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
