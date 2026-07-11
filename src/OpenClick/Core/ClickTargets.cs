using OpenClick.Native;

namespace OpenClick.Core;

/// <summary>
/// Interface for click targets. Perform one click action and return whether the target still exists.
/// </summary>
public interface IClickTarget
{
    /// <summary>
    /// Perform one click action (double = two clicks). Called on engine thread.
    /// </summary>
    /// <returns>false if the target is permanently gone (engine stops); true otherwise.</returns>
    bool PerformClick(ClickSettings settings, Random rng);
}

/// <summary>
/// Click target for the foreground window at the current cursor position or a fixed location.
/// </summary>
public sealed class ForegroundTarget : IClickTarget
{
    public ForegroundTarget()
    {
    }

    public bool PerformClick(ClickSettings settings, Random rng)
    {
        // If fixed position mode, set cursor to the fixed position first
        if (settings.PositionMode == PositionMode.Fixed)
        {
            NativeMethods.SetCursorPos(settings.FixedX, settings.FixedY);
        }

        // Map button to down/up flags
        (uint downFlag, uint upFlag) = settings.Button switch
        {
            MouseButton.Left => ((uint)NativeMethods.MOUSEEVENTF_LEFTDOWN, (uint)NativeMethods.MOUSEEVENTF_LEFTUP),
            MouseButton.Right => ((uint)NativeMethods.MOUSEEVENTF_RIGHTDOWN, (uint)NativeMethods.MOUSEEVENTF_RIGHTUP),
            MouseButton.Middle => ((uint)NativeMethods.MOUSEEVENTF_MIDDLEDOWN, (uint)NativeMethods.MOUSEEVENTF_MIDDLEUP),
            _ => ((uint)NativeMethods.MOUSEEVENTF_LEFTDOWN, (uint)NativeMethods.MOUSEEVENTF_LEFTUP),
        };

        // Send click
        NativeMethods.SendMouseClick(downFlag, upFlag);

        // If double click, send another click after a delay
        if (settings.ClickType == ClickType.Double)
        {
            int delayMs = Math.Clamp(settings.TotalIntervalMs / 4, 10, 50);
            Thread.Sleep(delayMs);
            NativeMethods.SendMouseClick(downFlag, upFlag);
        }

        // Foreground target always succeeds
        return true;
    }
}

/// <summary>
/// Click target for a specific window at a fixed point within its client area.
/// </summary>
public sealed class WindowTarget : IClickTarget
{
    private readonly WindowTargetInfo info;

    public WindowTarget(WindowTargetInfo info)
    {
        this.info = info;
    }

    public WindowTargetInfo Info => info;

    public bool PerformClick(ClickSettings settings, Random rng)
    {
        // Check if the window still exists
        if (!NativeMethods.IsWindow(info.Hwnd))
        {
            return false;
        }

        // Compute lParam from client coordinates
        IntPtr lParam = NativeMethods.MakeLParam(info.ClientX, info.ClientY);

        // Determine button-specific messages and wParam for down state
        (uint downMsg, uint upMsg, uint dblclkMsg, IntPtr wParamDown) = settings.Button switch
        {
            MouseButton.Left => (
                (uint)NativeMethods.WM_LBUTTONDOWN,
                (uint)NativeMethods.WM_LBUTTONUP,
                (uint)NativeMethods.WM_LBUTTONDBLCLK,
                (IntPtr)NativeMethods.MK_LBUTTON
            ),
            MouseButton.Right => (
                (uint)NativeMethods.WM_RBUTTONDOWN,
                (uint)NativeMethods.WM_RBUTTONUP,
                (uint)NativeMethods.WM_RBUTTONDBLCLK,
                (IntPtr)NativeMethods.MK_RBUTTON
            ),
            MouseButton.Middle => (
                (uint)NativeMethods.WM_MBUTTONDOWN,
                (uint)NativeMethods.WM_MBUTTONUP,
                (uint)NativeMethods.WM_MBUTTONDBLCLK,
                (IntPtr)NativeMethods.MK_MBUTTON
            ),
            _ => (
                (uint)NativeMethods.WM_LBUTTONDOWN,
                (uint)NativeMethods.WM_LBUTTONUP,
                (uint)NativeMethods.WM_LBUTTONDBLCLK,
                (IntPtr)NativeMethods.MK_LBUTTON
            ),
        };

        if (settings.ClickType == ClickType.Single)
        {
            // Single click: move, down, up
            NativeMethods.PostMessage(info.Hwnd, (uint)NativeMethods.WM_MOUSEMOVE, IntPtr.Zero, lParam);
            NativeMethods.PostMessage(info.Hwnd, downMsg, wParamDown, lParam);
            NativeMethods.PostMessage(info.Hwnd, upMsg, IntPtr.Zero, lParam);
        }
        else // Double
        {
            // Double click: move, down, up, dblclk, up
            NativeMethods.PostMessage(info.Hwnd, (uint)NativeMethods.WM_MOUSEMOVE, IntPtr.Zero, lParam);
            NativeMethods.PostMessage(info.Hwnd, downMsg, wParamDown, lParam);
            NativeMethods.PostMessage(info.Hwnd, upMsg, IntPtr.Zero, lParam);
            NativeMethods.PostMessage(info.Hwnd, dblclkMsg, wParamDown, lParam);
            NativeMethods.PostMessage(info.Hwnd, upMsg, IntPtr.Zero, lParam);
        }

        return true;
    }
}

/// <summary>
/// Click target for multiple windows. Posts clicks to all enabled targets.
/// </summary>
public sealed class MultiTarget : IClickTarget
{
    private readonly List<WindowTarget> targets;

    public MultiTarget(IReadOnlyList<WindowTargetInfo> targets)
    {
        this.targets = new();
        foreach (var targetInfo in targets)
        {
            if (targetInfo.Enabled)
            {
                this.targets.Add(new WindowTarget(targetInfo));
            }
        }
    }

    public bool PerformClick(ClickSettings settings, Random rng)
    {
        bool anyTargetAlive = false;

        // Post click to each target, tracking if any are still alive
        foreach (var target in targets)
        {
            // PerformClick returns false if window is gone
            if (!target.PerformClick(settings, rng))
            {
                // Target is dead; continue to others
            }
            else
            {
                anyTargetAlive = true;
            }
        }

        // Return false only if all targets are dead
        return anyTargetAlive;
    }
}
