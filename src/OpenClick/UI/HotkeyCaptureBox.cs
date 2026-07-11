using OpenClick.Core;
using OpenClick.Native;

namespace OpenClick.UI;

/// <summary>
/// Read-only TextBox subclass that captures a hotkey combo (modifiers + one non-modifier key).
/// While focused it suspends global hotkey dispatch (via <see cref="SuspendRequested"/>) so the
/// combo being typed doesn't also fire the currently-registered action.
/// </summary>
public sealed class HotkeyCaptureBox : TextBox
{
    private HotkeyCombo _combo = new();

    /// <summary>Raised whenever the captured combo changes (including clearing to empty via Esc).</summary>
    public event Action<HotkeyCombo>? ComboChanged;

    /// <summary>Raised with true on focus enter (suspend global hotkeys while capturing) and false on leave.</summary>
    public event Action<bool>? SuspendRequested;

    public HotkeyCaptureBox()
    {
        ReadOnly = true;
        Cursor = Cursors.Default;
        Text = _combo.ToDisplayString();
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    [System.ComponentModel.Browsable(false)]
    public HotkeyCombo Combo
    {
        get => _combo;
        set
        {
            _combo = value ?? new HotkeyCombo();
            RefreshDisplay();
        }
    }

    private void RefreshDisplay()
    {
        Text = _combo.ToDisplayString();
    }

    protected override void OnEnter(EventArgs e)
    {
        base.OnEnter(e);
        SuspendRequested?.Invoke(true);
        Text = "Press keys…";
    }

    protected override void OnLeave(EventArgs e)
    {
        base.OnLeave(e);
        SuspendRequested?.Invoke(false);
        RefreshDisplay();
    }

    protected override bool IsInputKey(Keys keyData)
    {
        // Let every key (Tab, arrows, Enter, Esc, ...) reach OnKeyDown instead of being
        // consumed for dialog navigation.
        return true;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (Focused && (keyData & Keys.Alt) == Keys.Alt)
        {
            HandleCandidateKey(keyData);
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        e.SuppressKeyPress = true;

        // Alt-combos are already handled in ProcessCmdKey; avoid double-processing here.
        if (!e.Alt)
        {
            HandleCandidateKey(e.KeyData);
        }

        base.OnKeyDown(e);
    }

    private void HandleCandidateKey(Keys keyData)
    {
        Keys keyCode = keyData & Keys.KeyCode;

        if (keyCode == Keys.Escape)
        {
            _combo = new HotkeyCombo();
            RefreshDisplay();
            ComboChanged?.Invoke(_combo);
            return;
        }

        if (IsModifierKey(keyCode))
        {
            // Wait for a non-modifier key before finalizing the combo.
            Text = "Press keys…";
            return;
        }

        bool win = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LWIN) & 0x8000) != 0 ||
                    (NativeMethods.GetAsyncKeyState(NativeMethods.VK_RWIN) & 0x8000) != 0;

        HotkeyCombo combo = new()
        {
            KeyCode = (int)keyCode,
            Ctrl = (keyData & Keys.Control) == Keys.Control,
            Shift = (keyData & Keys.Shift) == Keys.Shift,
            Alt = (keyData & Keys.Alt) == Keys.Alt,
            Win = win,
        };

        _combo = combo;
        RefreshDisplay();
        ComboChanged?.Invoke(_combo);
    }

    private static bool IsModifierKey(Keys keyCode) =>
        keyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin;
}
