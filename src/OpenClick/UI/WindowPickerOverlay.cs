using OpenClick.Native;

namespace OpenClick.UI;

/// <summary>
/// Visual half of a <see cref="OpenClick.Core.WindowPicker"/> session: a click-through,
/// non-activating highlight rectangle that tracks the window under the cursor, plus a
/// small floating info label. Both are driven by a ~30ms UI-thread timer.
/// </summary>
public sealed class WindowPickerOverlay : IDisposable
{
    private readonly System.Windows.Forms.Timer _timer;
    private readonly HighlightForm _highlightForm;
    private readonly InfoForm _infoForm;
    private bool _disposed;

    /// Deepest resolved window under the cursor, or IntPtr.Zero if none / it's our own window.
    public IntPtr HoveredChildHwnd { get; private set; }

    /// Top-level ancestor of <see cref="HoveredChildHwnd"/>.
    public IntPtr HoveredTopLevelHwnd { get; private set; }

    public WindowPickerOverlay()
    {
        _highlightForm = new HighlightForm();
        _infoForm = new InfoForm();
        _timer = new System.Windows.Forms.Timer { Interval = 30 };
        _timer.Tick += (_, _) => Tick();
    }

    public void Show()
    {
        _highlightForm.Show();
        _infoForm.Show();
        _timer.Start();
        Tick();
    }

    private void Tick()
    {
        NativeMethods.GetCursorPos(out POINT cursor);

        IntPtr hwnd = NativeMethods.WindowFromPoint(cursor);
        if (hwnd == IntPtr.Zero)
        {
            ClearHover();
            _infoForm.UpdateText(cursor, null);
            return;
        }

        IntPtr topLevel = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
        if (topLevel == IntPtr.Zero)
        {
            topLevel = hwnd;
        }

        NativeMethods.GetWindowThreadProcessId(topLevel, out uint pid);
        if (pid == (uint)Environment.ProcessId)
        {
            // Hovering one of OpenClick's own windows: show nothing.
            ClearHover();
            _infoForm.UpdateText(cursor, null);
            return;
        }

        // Resolve the deepest visible child under the cursor, recomputing the client
        // point from screen coordinates at each level.
        IntPtr current = topLevel;
        while (true)
        {
            POINT clientPt = cursor;
            NativeMethods.ScreenToClient(current, ref clientPt);
            IntPtr child = NativeMethods.RealChildWindowFromPoint(current, clientPt);
            if (child == IntPtr.Zero || child == current)
            {
                break;
            }

            current = child;
        }

        HoveredTopLevelHwnd = topLevel;
        HoveredChildHwnd = current;

        NativeMethods.GetWindowRect(current, out RECT rect);
        _highlightForm.SetHighlightRect(rect);

        POINT deepestClientPt = cursor;
        NativeMethods.ScreenToClient(current, ref deepestClientPt);
        string title = NativeMethods.GetWindowTitle(topLevel);
        string className = NativeMethods.GetWindowClass(current);
        _infoForm.UpdateText(cursor, $"{title} [{className}] ({deepestClientPt.X}, {deepestClientPt.Y})");
    }

    private void ClearHover()
    {
        HoveredTopLevelHwnd = IntPtr.Zero;
        HoveredChildHwnd = IntPtr.Zero;
        _highlightForm.ClearHighlight();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        _timer.Dispose();
        _highlightForm.Dispose();
        _infoForm.Dispose();
    }

    private const int WS_EX_LAYERED = 0x80000;
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int WS_EX_TOOLWINDOW = 0x80;
    private const int WS_EX_NOACTIVATE = 0x8000000;

    /// <summary>Borderless, click-through, non-activating semi-transparent highlight rectangle.</summary>
    private sealed class HighlightForm : Form
    {
        public HighlightForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.DeepSkyBlue;
            Opacity = 0.35;
            Bounds = Rectangle.Empty;
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        public void SetHighlightRect(RECT rect)
        {
            int width = Math.Max(0, rect.Right - rect.Left);
            int height = Math.Max(0, rect.Bottom - rect.Top);
            Bounds = new Rectangle(rect.Left, rect.Top, width, height);
            if (!Visible)
            {
                Visible = true;
            }
        }

        public void ClearHighlight()
        {
            if (Visible)
            {
                Visible = false;
            }
        }
    }

    /// <summary>Small floating, click-through, non-activating tooltip-style info label.</summary>
    private sealed class InfoForm : Form
    {
        private const int CursorOffsetX = 18;
        private const int CursorOffsetY = 18;

        private readonly Label _label;

        public InfoForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            BackColor = Color.LightYellow;
            Padding = new Padding(4);

            _label = new Label
            {
                AutoSize = true,
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.Black,
                Text = string.Empty,
                Location = new Point(4, 4),
            };
            Controls.Add(_label);
            Visible = false;
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        public void UpdateText(POINT cursor, string? info)
        {
            if (info == null)
            {
                if (Visible)
                {
                    Visible = false;
                }

                return;
            }

            _label.Text = $"{info} — click to select, Esc to cancel";
            AutoSize = true;

            Point location = new(cursor.X + CursorOffsetX, cursor.Y + CursorOffsetY);
            Rectangle screen = Screen.FromPoint(new Point(cursor.X, cursor.Y)).Bounds;
            if (location.X + Width > screen.Right)
            {
                location.X = screen.Right - Width;
            }

            if (location.Y + Height > screen.Bottom)
            {
                location.Y = screen.Bottom - Height;
            }

            Location = location;

            if (!Visible)
            {
                Visible = true;
            }
        }
    }
}
