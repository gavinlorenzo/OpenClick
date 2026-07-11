using System.Runtime.InteropServices;
using OpenClick.Native;
using OpenClick.UI;

namespace OpenClick.Core;

/// <summary>
/// One-shot "pick a background window/point" session. Shows a click-through highlight
/// overlay that tracks the window under the cursor; left-click selects it (and is
/// swallowed so the click never reaches the target window), Esc cancels.
/// </summary>
public sealed class WindowPicker
{
    public static WindowTargetInfo? PickTarget(IWin32Window owner)
    {
        WindowTargetInfo? result = null;
        bool done = false;

        WindowPickerOverlay? overlay = null;
        LowLevelProc? mouseProc = null;
        LowLevelProc? keyboardProc = null;
        IntPtr mouseHook = IntPtr.Zero;
        IntPtr keyboardHook = IntPtr.Zero;

        try
        {
            overlay = new WindowPickerOverlay();

            IntPtr moduleHandle = NativeMethods.GetModuleHandle(null);

            mouseProc = (nCode, wParam, lParam) =>
            {
                if (nCode >= 0 && !done)
                {
                    int msg = (int)wParam;
                    if (msg == NativeMethods.WM_LBUTTONDOWN)
                    {
                        result = BuildSelection(overlay);
                        done = true;
                        return (IntPtr)1;
                    }

                    if (msg == NativeMethods.WM_LBUTTONUP)
                    {
                        // Swallow the matching button-up of the click we just consumed.
                        if (done)
                        {
                            return (IntPtr)1;
                        }
                    }
                }

                return NativeMethods.CallNextHookEx(mouseHook, nCode, wParam, lParam);
            };

            keyboardProc = (nCode, wParam, lParam) =>
            {
                if (nCode >= 0 && !done)
                {
                    int msg = (int)wParam;
                    if (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN)
                    {
                        var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                        if ((int)info.vkCode == NativeMethods.VK_ESCAPE)
                        {
                            result = null;
                            done = true;
                            return (IntPtr)1;
                        }
                    }
                }

                return NativeMethods.CallNextHookEx(keyboardHook, nCode, wParam, lParam);
            };

            mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, mouseProc, moduleHandle, 0);
            keyboardHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, keyboardProc, moduleHandle, 0);

            overlay.Show();

            while (!done)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }

            return result;
        }
        finally
        {
            if (mouseHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(mouseHook);
            }

            if (keyboardHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(keyboardHook);
            }

            overlay?.Dispose();
        }
    }

    private static WindowTargetInfo? BuildSelection(WindowPickerOverlay overlay)
    {
        IntPtr topLevel = overlay.HoveredTopLevelHwnd;
        IntPtr deepestChild = overlay.HoveredChildHwnd;

        if (deepestChild == IntPtr.Zero || topLevel == IntPtr.Zero)
        {
            return null;
        }

        NativeMethods.GetCursorPos(out POINT cursor);
        POINT clientPt = cursor;
        NativeMethods.ScreenToClient(deepestChild, ref clientPt);

        return new WindowTargetInfo
        {
            HwndValue = (long)deepestChild,
            WindowTitle = NativeMethods.GetWindowTitle(topLevel),
            ClassName = NativeMethods.GetWindowClass(deepestChild),
            ClientX = clientPt.X,
            ClientY = clientPt.Y,
            Enabled = true,
        };
    }
}
