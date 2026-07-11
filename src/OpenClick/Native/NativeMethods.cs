using System;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenClick.Native;

/// <summary>Low-level hook callback delegate (WH_KEYBOARD_LL / WH_MOUSE_LL).</summary>
public delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

[StructLayout(LayoutKind.Sequential)]
public struct POINT
{
    public int X;
    public int Y;
}

[StructLayout(LayoutKind.Sequential)]
public struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}

[StructLayout(LayoutKind.Sequential)]
public struct MOUSEINPUT
{
    public int dx;
    public int dy;
    public uint mouseData;
    public uint dwFlags;
    public uint time;
    public IntPtr dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
public struct KEYBDINPUT
{
    public ushort wVk;
    public ushort wScan;
    public uint dwFlags;
    public uint time;
    public IntPtr dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
public struct HARDWAREINPUT
{
    public uint uMsg;
    public ushort wParamL;
    public ushort wParamH;
}

[StructLayout(LayoutKind.Explicit)]
public struct InputUnion
{
    [FieldOffset(0)] public MOUSEINPUT mi;
    [FieldOffset(0)] public KEYBDINPUT ki;
    [FieldOffset(0)] public HARDWAREINPUT hi;
}

[StructLayout(LayoutKind.Sequential)]
public struct INPUT
{
    public uint type;
    public InputUnion U;
}

[StructLayout(LayoutKind.Sequential)]
public struct MSLLHOOKSTRUCT
{
    public POINT pt;
    public uint mouseData;
    public uint flags;
    public uint time;
    public IntPtr dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
public struct KBDLLHOOKSTRUCT
{
    public uint vkCode;
    public uint scanCode;
    public uint flags;
    public uint time;
    public IntPtr dwExtraInfo;
}

public static class NativeMethods
{
    // ---- Hook constants ----
    public const int WH_KEYBOARD_LL = 13;
    public const int WH_MOUSE_LL = 14;
    public const int HC_ACTION = 0;

    // ---- Low-level hook flags ----
    public const int LLKHF_INJECTED = 0x10;
    public const int LLMHF_INJECTED = 0x1;

    // ---- Window messages ----
    public const int WM_KEYDOWN = 0x100;
    public const int WM_KEYUP = 0x101;
    public const int WM_SYSKEYDOWN = 0x104;
    public const int WM_SYSKEYUP = 0x105;
    public const int WM_MOUSEMOVE = 0x200;
    public const int WM_LBUTTONDOWN = 0x201;
    public const int WM_LBUTTONUP = 0x202;
    public const int WM_LBUTTONDBLCLK = 0x203;
    public const int WM_RBUTTONDOWN = 0x204;
    public const int WM_RBUTTONUP = 0x205;
    public const int WM_RBUTTONDBLCLK = 0x206;
    public const int WM_MBUTTONDOWN = 0x207;
    public const int WM_MBUTTONUP = 0x208;
    public const int WM_MBUTTONDBLCLK = 0x209;
    public const int WM_MOUSEWHEEL = 0x20A;
    public const int WM_XBUTTONDOWN = 0x20B;
    public const int WM_XBUTTONUP = 0x20C;

    // ---- Mouse-key wParam flags ----
    public const int MK_LBUTTON = 0x1;
    public const int MK_RBUTTON = 0x2;
    public const int MK_MBUTTON = 0x10;

    // ---- SendInput ----
    public const int INPUT_MOUSE = 0;
    public const int INPUT_KEYBOARD = 1;
    public const int MOUSEEVENTF_MOVE = 0x1;
    public const int MOUSEEVENTF_LEFTDOWN = 0x2;
    public const int MOUSEEVENTF_LEFTUP = 0x4;
    public const int MOUSEEVENTF_RIGHTDOWN = 0x8;
    public const int MOUSEEVENTF_RIGHTUP = 0x10;
    public const int MOUSEEVENTF_MIDDLEDOWN = 0x20;
    public const int MOUSEEVENTF_MIDDLEUP = 0x40;
    public const int MOUSEEVENTF_WHEEL = 0x800;
    public const int MOUSEEVENTF_VIRTUALDESK = 0x4000;
    public const int MOUSEEVENTF_ABSOLUTE = 0x8000;
    public const int KEYEVENTF_KEYUP = 0x2;

    // ---- Virtual keys ----
    public const int VK_SHIFT = 0x10;
    public const int VK_CONTROL = 0x11;
    public const int VK_MENU = 0x12;
    public const int VK_LWIN = 0x5B;
    public const int VK_RWIN = 0x5C;
    public const int VK_ESCAPE = 0x1B;

    // ---- System metrics ----
    public const int SM_XVIRTUALSCREEN = 76;
    public const int SM_YVIRTUALSCREEN = 77;
    public const int SM_CXVIRTUALSCREEN = 78;
    public const int SM_CYVIRTUALSCREEN = 79;

    // ---- Misc ----
    public const int GA_ROOT = 2;
    public const int CWP_SKIPINVISIBLE = 0x1;
    public const int CWP_SKIPTRANSPARENT = 0x4;
    public const int SMTO_ABORTIFHUNG = 0x2;

    // ---- user32: input ----
    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    public static extern uint MapVirtualKey(uint uCode, uint uMapType);

    // ---- user32: hooks ----
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    // ---- user32: messaging ----
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
        uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    // ---- user32: window queries ----
    [DllImport("user32.dll")]
    public static extern IntPtr WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    public static extern IntPtr RealChildWindowFromPoint(IntPtr hwndParent, POINT ptParentClientCoords);

    [DllImport("user32.dll")]
    public static extern IntPtr ChildWindowFromPointEx(IntPtr hwnd, POINT pt, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    public static extern uint GetDoubleClickTime();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    // ---- kernel32 ----
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    // ---- winmm ----
    [DllImport("winmm.dll")]
    public static extern uint timeBeginPeriod(uint uPeriod);

    [DllImport("winmm.dll")]
    public static extern uint timeEndPeriod(uint uPeriod);

    // ---- Helpers ----

    /// <summary>Packs two 16-bit values into an lParam: (hiWord &lt;&lt; 16) | (loWord &amp; 0xFFFF).</summary>
    public static IntPtr MakeLParam(int loWord, int hiWord) =>
        (IntPtr)((hiWord << 16) | (loWord & 0xFFFF));

    /// <summary>Sends a mouse button down + up pair as two INPUTs in a single SendInput call.</summary>
    public static void SendMouseClick(uint downFlag, uint upFlag)
    {
        var inputs = new INPUT[2];
        inputs[0].type = INPUT_MOUSE;
        inputs[0].U.mi.dwFlags = downFlag;
        inputs[1].type = INPUT_MOUSE;
        inputs[1].U.mi.dwFlags = upFlag;
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    /// <summary>Gets a window's title text, or "" if none.</summary>
    public static string GetWindowTitle(IntPtr hWnd)
    {
        int length = GetWindowTextLength(hWnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder(length + 1);
        int copied = GetWindowText(hWnd, sb, sb.Capacity);
        return copied > 0 ? sb.ToString() : string.Empty;
    }

    /// <summary>Gets a window's class name, or "" on failure.</summary>
    public static string GetWindowClass(IntPtr hWnd)
    {
        var sb = new StringBuilder(256);
        int copied = GetClassName(hWnd, sb, sb.Capacity);
        return copied > 0 ? sb.ToString() : string.Empty;
    }
}
