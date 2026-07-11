namespace OpenClick.UI;

/// <summary>
/// Fullscreen point-picker overlay: borderless, TopMost, 25% opacity black, covering the
/// virtual screen. Left-click returns the screen point under the cursor; Esc cancels.
/// </summary>
public sealed class PickLocationOverlay : Form
{
    private readonly Label _label;
    private Point? _result;

    private PickLocationOverlay()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.Black;
        Opacity = 0.25;
        Cursor = Cursors.Cross;
        Bounds = SystemInformation.VirtualScreen;
        KeyPreview = true;
        DoubleBuffered = true;

        _label = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.White,
            BackColor = Color.FromArgb(200, 20, 20, 20),
            Padding = new Padding(5, 3, 5, 3),
            Text = "click to select, Esc to cancel",
        };
        Controls.Add(_label);

        MouseMove += OnOverlayMouseMove;
        MouseClick += OnOverlayMouseClick;
        KeyDown += OnOverlayKeyDown;
    }

    /// <summary>Shows the overlay modally and returns the picked screen point, or null on cancel.</summary>
    public static Point? Pick()
    {
        using PickLocationOverlay overlay = new();
        overlay.ShowDialog();
        return overlay._result;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        PositionLabelNear(PointToClient(Cursor.Position));
    }

    private void OnOverlayMouseMove(object? sender, MouseEventArgs e)
    {
        Point screenPt = PointToScreen(e.Location);
        _label.Text = $"{screenPt.X}, {screenPt.Y} — click to select, Esc to cancel";
        PositionLabelNear(e.Location);
    }

    private void PositionLabelNear(Point clientPoint)
    {
        const int offset = 20;
        Point location = new(clientPoint.X + offset, clientPoint.Y + offset);

        if (location.X + _label.Width > ClientSize.Width)
        {
            location.X = clientPoint.X - _label.Width - offset;
        }

        if (location.Y + _label.Height > ClientSize.Height)
        {
            location.Y = clientPoint.Y - _label.Height - offset;
        }

        _label.Location = location;
    }

    private void OnOverlayMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _result = PointToScreen(e.Location);
            Close();
        }
    }

    private void OnOverlayKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            _result = null;
            Close();
        }
    }
}
