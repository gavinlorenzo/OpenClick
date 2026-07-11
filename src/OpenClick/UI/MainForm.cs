using System.Diagnostics;
using OpenClick.Core;

namespace OpenClick.UI;

/// <summary>Main OpenClick window: 4 tabs (Autoclick / Record & Playback / Background & Multiclick / Settings).</summary>
public sealed class MainForm : Form
{
    private enum EngineMode { None, Foreground, Background }

    // ---- Core services (owned here) ----
    private readonly ClickEngine _engine = new();
    private readonly HotkeyManager _hotkeyManager = new();
    private readonly HoldClickMonitor _holdMonitor = new();
    private readonly Recorder _recorder = new();
    private readonly Player _player = new();

    private AppSettings _settings = new();
    private MacroScript? _currentScript;
    private EngineMode _engineMode = EngineMode.None;

    // ---- StatusStrip ----
    private readonly StatusStrip _statusStrip = new();
    private readonly ToolStripStatusLabel _lblState = new() { Text = "Idle", Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel _lblHotkeyReminder = new();

    // ---- Tab 1: Autoclick ----
    private readonly TabControl _tabControl = new() { Dock = DockStyle.Fill };
    private readonly TabPage _tabAutoclick = new("Autoclick");
    private readonly TabPage _tabRecord = new("Record && Playback");
    private readonly TabPage _tabBackground = new("Background && Multiclick");
    private readonly TabPage _tabSettings = new("Settings");

    private readonly NumericUpDown _numHours = new() { Minimum = 0, Maximum = 23, Width = 50 };
    private readonly NumericUpDown _numMinutes = new() { Minimum = 0, Maximum = 59, Width = 50 };
    private readonly NumericUpDown _numSeconds = new() { Minimum = 0, Maximum = 59, Width = 50 };
    private readonly NumericUpDown _numMs = new() { Minimum = 0, Maximum = 9999, Width = 60 };
    private readonly CheckBox _chkRandomOffset = new() { Text = "Random offset ±", AutoSize = true };
    private readonly NumericUpDown _numRandomOffsetMs = new() { Minimum = 0, Maximum = 60000, Width = 70 };

    private readonly ComboBox _cboButton = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
    private readonly ComboBox _cboClickType = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
    private readonly CheckBox _chkHoldMode = new()
    {
        Text = "Hold mode — autoclick while the trigger mouse button is physically held",
        AutoSize = true,
    };

    private readonly RadioButton _radRepeatUntilStopped = new() { Text = "Repeat until stopped", AutoSize = true, Checked = true };
    private readonly RadioButton _radRepeatCount = new() { Text = "Repeat", AutoSize = true };
    private readonly NumericUpDown _numRepeatCount = new() { Minimum = 1, Maximum = 1000000, Width = 80, Value = 100 };

    private readonly RadioButton _radPosCurrent = new() { Text = "Current position", AutoSize = true, Checked = true };
    private readonly RadioButton _radPosFixed = new() { Text = "Fixed", AutoSize = true };
    private readonly NumericUpDown _numFixedX = new() { Minimum = -32768, Maximum = 32767, Width = 65 };
    private readonly NumericUpDown _numFixedY = new() { Minimum = -32768, Maximum = 32767, Width = 65 };
    private readonly Button _btnPickLocation = new() { Text = "Pick location..." };

    private readonly Button _btnStart = new() { Text = "Start" };
    private readonly Button _btnStop = new() { Text = "Stop", Enabled = false };

    // ---- Tab 2: Record & Playback ----
    private readonly Button _btnRecordToggle = new() { Text = "● Record" };
    private readonly Label _lblEventCount = new() { Text = "0 events recorded", AutoSize = true };

    private readonly ComboBox _cboSpeed = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 80 };
    private readonly NumericUpDown _numRepeatPlayback = new() { Minimum = 0, Maximum = 10000, Width = 70, Value = 1 };
    private readonly Button _btnPlayToggle = new() { Text = "▶ Play", Enabled = false };
    private readonly Label _lblPlaybackProgress = new() { Text = "Idle", AutoSize = true };

    private readonly Button _btnSaveMacro = new() { Text = "Save macro..." };
    private readonly Button _btnLoadMacro = new() { Text = "Load macro..." };
    private readonly Label _lblMacroInfo = new() { Text = "No macro loaded", AutoSize = true };

    // ---- Tab 3: Background & Multiclick ----
    private readonly ListView _lvTargets = new()
    {
        View = View.Details,
        CheckBoxes = true,
        FullRowSelect = true,
        HideSelection = false,
    };
    private readonly Button _btnAddTarget = new() { Text = "Add target... (pick)" };
    private readonly Button _btnRemoveTarget = new() { Text = "Remove" };
    private readonly Button _btnTestClick = new() { Text = "Test click" };
    private readonly Button _btnStartBackground = new() { Text = "Start background clicking" };
    private readonly Button _btnStopBackground = new() { Text = "Stop background clicking", Enabled = false };

    // ---- Tab 4: Settings ----
    private readonly HotkeyCaptureBox _hkClicker = new();
    private readonly HotkeyCaptureBox _hkRecord = new();
    private readonly HotkeyCaptureBox _hkPlayback = new();
    private readonly CheckBox _chkAlwaysOnTop = new() { Text = "Always on top", AutoSize = true };
    private readonly LinkLabel _lnkGithub = new() { Text = "OpenClick on GitHub", AutoSize = true };

    public MainForm()
    {
        Text = "OpenClick";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(540, 460);
        Font = new Font("Segoe UI", 9f);

        BuildAutoclickTab();
        BuildRecordTab();
        BuildBackgroundTab();
        BuildSettingsTab();

        _tabControl.TabPages.AddRange(new[] { _tabAutoclick, _tabRecord, _tabBackground, _tabSettings });

        _statusStrip.Items.Add(_lblState);
        _statusStrip.Items.Add(_lblHotkeyReminder);

        Controls.Add(_tabControl);
        Controls.Add(_statusStrip);

        WireCoreEvents();
        WireControlEvents();

        _settings = SettingsStore.Load();
        ApplySettingsToControls(_settings);
        RegisterHotkeysFromSettings();
        UpdateButtonLabels();
        UpdateStatusHotkeyLabel();
        UpdateEngineButtons();
        UpdatePlayButtonEnabled();

        FormClosing += OnMainFormClosing;
        FormClosed += OnMainFormClosed;
    }

    // ================================================================
    // Tab building
    // ================================================================

    private void BuildAutoclickTab()
    {
        GroupBox grpInterval = new() { Text = "Click interval", Bounds = new Rectangle(10, 10, 512, 90) };
        Label lblHours = new() { Text = "Hours", AutoSize = true, Location = new Point(10, 27) };
        _numHours.Location = new Point(60, 24);
        Label lblMinutes = new() { Text = "Mins", AutoSize = true, Location = new Point(125, 27) };
        _numMinutes.Location = new Point(170, 24);
        Label lblSeconds = new() { Text = "Secs", AutoSize = true, Location = new Point(235, 27) };
        _numSeconds.Location = new Point(280, 24);
        Label lblMs = new() { Text = "Ms", AutoSize = true, Location = new Point(345, 27) };
        _numMs.Location = new Point(380, 24);
        _chkRandomOffset.Location = new Point(10, 58);
        _numRandomOffsetMs.Location = new Point(155, 56);
        Label lblOffsetMs = new() { Text = "ms", AutoSize = true, Location = new Point(230, 58) };
        grpInterval.Controls.AddRange(new Control[]
        {
            lblHours, _numHours, lblMinutes, _numMinutes, lblSeconds, _numSeconds, lblMs, _numMs,
            _chkRandomOffset, _numRandomOffsetMs, lblOffsetMs,
        });

        GroupBox grpOptions = new() { Text = "Click options", Bounds = new Rectangle(10, 108, 512, 80) };
        Label lblButton = new() { Text = "Mouse button:", AutoSize = true, Location = new Point(10, 27) };
        _cboButton.Location = new Point(105, 24);
        _cboButton.Items.AddRange(new object[] { "Left", "Right", "Middle" });
        Label lblClickType = new() { Text = "Click type:", AutoSize = true, Location = new Point(230, 27) };
        _cboClickType.Location = new Point(300, 24);
        _cboClickType.Items.AddRange(new object[] { "Single", "Double" });
        _chkHoldMode.Location = new Point(10, 54);
        grpOptions.Controls.AddRange(new Control[] { lblButton, _cboButton, lblClickType, _cboClickType, _chkHoldMode });

        GroupBox grpRepeat = new() { Text = "Click repeat", Bounds = new Rectangle(10, 196, 250, 120) };
        _radRepeatUntilStopped.Location = new Point(10, 24);
        _radRepeatCount.Location = new Point(10, 52);
        _numRepeatCount.Location = new Point(75, 50);
        Label lblTimes = new() { Text = "times", AutoSize = true, Location = new Point(160, 52) };
        grpRepeat.Controls.AddRange(new Control[] { _radRepeatUntilStopped, _radRepeatCount, _numRepeatCount, lblTimes });

        GroupBox grpPosition = new() { Text = "Cursor position", Bounds = new Rectangle(270, 196, 252, 120) };
        _radPosCurrent.Location = new Point(10, 24);
        _radPosFixed.Location = new Point(10, 52);
        Label lblX = new() { Text = "X:", AutoSize = true, Location = new Point(25, 80) };
        _numFixedX.Location = new Point(45, 77);
        Label lblY = new() { Text = "Y:", AutoSize = true, Location = new Point(120, 80) };
        _numFixedY.Location = new Point(140, 77);
        _btnPickLocation.Bounds = new Rectangle(10, 105, 232, 26);
        grpPosition.Controls.AddRange(new Control[] { _radPosCurrent, _radPosFixed, lblX, _numFixedX, lblY, _numFixedY, _btnPickLocation });

        _btnStart.Bounds = new Rectangle(10, 328, 246, 40);
        _btnStop.Bounds = new Rectangle(276, 328, 246, 40);

        _tabAutoclick.Controls.AddRange(new Control[] { grpInterval, grpOptions, grpRepeat, grpPosition, _btnStart, _btnStop });
    }

    private void BuildRecordTab()
    {
        GroupBox grpRecording = new() { Text = "Recording", Bounds = new Rectangle(10, 10, 512, 65) };
        _btnRecordToggle.Bounds = new Rectangle(10, 24, 170, 28);
        _lblEventCount.Location = new Point(195, 30);
        grpRecording.Controls.AddRange(new Control[] { _btnRecordToggle, _lblEventCount });

        GroupBox grpPlayback = new() { Text = "Playback", Bounds = new Rectangle(10, 85, 512, 130) };
        Label lblSpeed = new() { Text = "Speed:", AutoSize = true, Location = new Point(10, 30) };
        _cboSpeed.Location = new Point(65, 27);
        _cboSpeed.Items.AddRange(new object[] { "0.25x", "0.5x", "1x", "2x", "4x" });
        _cboSpeed.SelectedIndex = 2;
        Label lblRepeat = new() { Text = "Repeat:", AutoSize = true, Location = new Point(165, 30) };
        _numRepeatPlayback.Location = new Point(220, 27);
        Label lblRepeatNote = new() { Text = "(0 = repeat forever)", AutoSize = true, Location = new Point(300, 30) };
        _btnPlayToggle.Bounds = new Rectangle(10, 65, 170, 28);
        _lblPlaybackProgress.Location = new Point(195, 71);
        grpPlayback.Controls.AddRange(new Control[]
        {
            lblSpeed, _cboSpeed, lblRepeat, _numRepeatPlayback, lblRepeatNote, _btnPlayToggle, _lblPlaybackProgress,
        });

        GroupBox grpFile = new() { Text = "Macro file", Bounds = new Rectangle(10, 225, 512, 70) };
        _btnSaveMacro.Bounds = new Rectangle(10, 26, 150, 28);
        _btnLoadMacro.Bounds = new Rectangle(170, 26, 150, 28);
        _lblMacroInfo.Location = new Point(335, 32);
        grpFile.Controls.AddRange(new Control[] { _btnSaveMacro, _btnLoadMacro, _lblMacroInfo });

        _tabRecord.Controls.AddRange(new Control[] { grpRecording, grpPlayback, grpFile });
    }

    private void BuildBackgroundTab()
    {
        _lvTargets.Bounds = new Rectangle(10, 10, 512, 215);
        _lvTargets.Columns.Add("Window", 240);
        _lvTargets.Columns.Add("Class", 120);
        _lvTargets.Columns.Add("Point", 100);

        _btnAddTarget.Bounds = new Rectangle(10, 232, 150, 28);
        _btnRemoveTarget.Bounds = new Rectangle(170, 232, 100, 28);
        _btnTestClick.Bounds = new Rectangle(280, 232, 110, 28);

        Label lblNote = new()
        {
            Text = "Background clicks use PostMessage; some apps (games using raw input) ignore them.",
            AutoSize = false,
            Bounds = new Rectangle(10, 266, 512, 32),
            ForeColor = SystemColors.GrayText,
        };

        _btnStartBackground.Bounds = new Rectangle(10, 304, 246, 34);
        _btnStopBackground.Bounds = new Rectangle(276, 304, 246, 34);

        _tabBackground.Controls.AddRange(new Control[]
        {
            _lvTargets, _btnAddTarget, _btnRemoveTarget, _btnTestClick, lblNote, _btnStartBackground, _btnStopBackground,
        });
    }

    private void BuildSettingsTab()
    {
        Label lblClicker = new() { Text = "Toggle clicker:", AutoSize = true, Location = new Point(10, 23) };
        _hkClicker.Bounds = new Rectangle(150, 20, 170, 23);
        Label lblRecord = new() { Text = "Toggle recording:", AutoSize = true, Location = new Point(10, 58) };
        _hkRecord.Bounds = new Rectangle(150, 55, 170, 23);
        Label lblPlayback = new() { Text = "Toggle playback:", AutoSize = true, Location = new Point(10, 93) };
        _hkPlayback.Bounds = new Rectangle(150, 90, 170, 23);

        _chkAlwaysOnTop.Location = new Point(10, 135);
        _lnkGithub.Location = new Point(10, 170);
        _lnkGithub.LinkClicked += OnGithubLinkClicked;

        _tabSettings.Controls.AddRange(new Control[]
        {
            lblClicker, _hkClicker, lblRecord, _hkRecord, lblPlayback, _hkPlayback, _chkAlwaysOnTop, _lnkGithub,
        });
    }

    // ================================================================
    // Wiring
    // ================================================================

    private void WireCoreEvents()
    {
        _engine.Started += () => UiInvoke(() => { UpdateEngineButtons(); UpdateStatusLabel(); });
        _engine.Stopped += () => UiInvoke(() => { UpdateEngineButtons(); UpdateStatusLabel(); });
        _engine.ClickTick += n => UiInvoke(() => { _lblState.Text = $"Clicking… ({n})"; });

        _hotkeyManager.HotkeyPressed += actionId => UiInvoke(() => OnHotkeyPressed(actionId));

        _holdMonitor.HoldStarted += () => UiInvoke(StartForeground);
        _holdMonitor.HoldEnded += () => UiInvoke(StopEngine);

        _recorder.EventCaptured += n => UiInvoke(() =>
        {
            _lblEventCount.Text = $"{n} events captured";
            _lblState.Text = $"Recording… ({n} events)";
        });

        _player.PlaybackStarted += () => UiInvoke(() =>
        {
            _btnPlayToggle.Text = $"■ Stop ({_settings.TogglePlaybackHotkey.ToDisplayString()})";
            UpdateStatusLabel();
        });
        _player.PlaybackFinished += () => UiInvoke(() =>
        {
            UpdatePlayButtonEnabled();
            _btnPlayToggle.Text = PlayButtonLabel();
            _lblPlaybackProgress.Text = "Idle";
            UpdateStatusLabel();
        });
        _player.RepeatCompleted += n => UiInvoke(() => { _lblPlaybackProgress.Text = $"Repeat {n} completed"; });
    }

    private void WireControlEvents()
    {
        _btnStart.Click += (_, _) => StartForeground();
        _btnStop.Click += (_, _) => StopEngine();
        _chkHoldMode.CheckedChanged += (_, _) =>
        {
            _holdMonitor.Enabled = _chkHoldMode.Checked;
            UpdateEngineButtons();
        };
        _cboButton.SelectedIndexChanged += (_, _) => _holdMonitor.TriggerButton = GetSelectedMouseButton();
        _btnPickLocation.Click += (_, _) =>
        {
            Point? pt = PickLocationOverlay.Pick();
            if (pt.HasValue)
            {
                _radPosFixed.Checked = true;
                _numFixedX.Value = Math.Clamp(pt.Value.X, -32768, 32767);
                _numFixedY.Value = Math.Clamp(pt.Value.Y, -32768, 32767);
            }
        };

        _btnRecordToggle.Click += (_, _) => { if (_recorder.IsRecording) StopRecording(); else StartRecording(); };
        _btnPlayToggle.Click += (_, _) => { if (_player.IsPlaying) StopPlayback(); else StartPlayback(); };
        _btnSaveMacro.Click += OnSaveMacroClick;
        _btnLoadMacro.Click += OnLoadMacroClick;

        _lvTargets.ItemChecked += (_, e) =>
        {
            if (e.Item.Tag is WindowTargetInfo info)
            {
                info.Enabled = e.Item.Checked;
            }
        };
        _btnAddTarget.Click += OnAddTargetClick;
        _btnRemoveTarget.Click += (_, _) =>
        {
            foreach (ListViewItem item in _lvTargets.SelectedItems.Cast<ListViewItem>().ToList())
            {
                _lvTargets.Items.Remove(item);
            }
        };
        _btnTestClick.Click += (_, _) =>
        {
            if (_lvTargets.SelectedItems.Count > 0 && _lvTargets.SelectedItems[0].Tag is WindowTargetInfo info)
            {
                new WindowTarget(info).PerformClick(BuildClickSettingsFromTab1(), new Random());
            }
        };
        _btnStartBackground.Click += (_, _) => StartBackground();
        _btnStopBackground.Click += (_, _) => StopEngine();

        _hkClicker.SuspendRequested += suspended => _hotkeyManager.Suspended = suspended;
        _hkRecord.SuspendRequested += suspended => _hotkeyManager.Suspended = suspended;
        _hkPlayback.SuspendRequested += suspended => _hotkeyManager.Suspended = suspended;
        _hkClicker.ComboChanged += combo => OnHotkeyComboChanged("toggle-clicker", combo, c => _settings.ToggleClickerHotkey = c);
        _hkRecord.ComboChanged += combo => OnHotkeyComboChanged("toggle-record", combo, c => _settings.ToggleRecordHotkey = c);
        _hkPlayback.ComboChanged += combo => OnHotkeyComboChanged("toggle-playback", combo, c => _settings.TogglePlaybackHotkey = c);

        _chkAlwaysOnTop.CheckedChanged += (_, _) => TopMost = _chkAlwaysOnTop.Checked;
    }

    private void OnHotkeyComboChanged(string actionId, HotkeyCombo combo, Action<HotkeyCombo> assign)
    {
        assign(combo);
        if (combo.IsEmpty)
        {
            _hotkeyManager.Unregister(actionId);
        }
        else
        {
            _hotkeyManager.Register(actionId, combo);
        }

        UpdateButtonLabels();
        UpdateStatusHotkeyLabel();
    }

    private void OnGithubLinkClicked(object? sender, LinkLabelLinkClickedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://github.com/") { UseShellExecute = true });
        }
        catch
        {
            // Ignore failures to launch a browser.
        }
    }

    private void OnAddTargetClick(object? sender, EventArgs e)
    {
        Hide();
        WindowTargetInfo? picked;
        try
        {
            picked = WindowPicker.PickTarget(this);
        }
        finally
        {
            Show();
        }

        if (picked != null)
        {
            AddTargetToList(picked);
        }
    }

    private void OnSaveMacroClick(object? sender, EventArgs e)
    {
        if (_currentScript == null || _currentScript.Events.Count == 0)
        {
            MessageBox.Show(this, "No macro to save. Record something first.", "OpenClick", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using SaveFileDialog dlg = new()
        {
            Filter = "OpenClick macro (*.ocmacro.json)|*.ocmacro.json|All files (*.*)|*.*",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            FileName = "macro.ocmacro.json",
        };

        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            try
            {
                _currentScript.Save(dlg.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to save macro: {ex.Message}", "OpenClick", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private void OnLoadMacroClick(object? sender, EventArgs e)
    {
        using OpenFileDialog dlg = new()
        {
            Filter = "OpenClick macro (*.ocmacro.json)|*.ocmacro.json|All files (*.*)|*.*",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };

        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            try
            {
                _currentScript = MacroScript.Load(dlg.FileName);
                _lblMacroInfo.Text = $"Loaded {_currentScript.Events.Count} events";
                _lblEventCount.Text = $"{_currentScript.Events.Count} events recorded";
                UpdatePlayButtonEnabled();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to load macro: {ex.Message}", "OpenClick", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    // ================================================================
    // Hotkey dispatch
    // ================================================================

    private void OnHotkeyPressed(string actionId)
    {
        switch (actionId)
        {
            case "toggle-clicker":
                if (_engine.IsRunning) StopEngine(); else StartForeground();
                break;
            case "toggle-record":
                if (_recorder.IsRecording) StopRecording(); else StartRecording();
                break;
            case "toggle-playback":
                if (_player.IsPlaying) StopPlayback(); else StartPlayback();
                break;
        }
    }

    // ================================================================
    // Engine / recorder / player actions
    // ================================================================

    private void StartForeground()
    {
        if (_recorder.IsRecording) StopRecording();
        if (_engine.IsRunning) _engine.Stop();

        _engineMode = EngineMode.Foreground;
        _engine.Start(BuildClickSettingsFromTab1(), new ForegroundTarget());
        UpdateEngineButtons();
        UpdateStatusLabel();
    }

    private void StartBackground()
    {
        List<WindowTargetInfo> targets = GetBackgroundTargetInfos();
        if (targets.Count == 0)
        {
            MessageBox.Show(this, "Add at least one background target first.", "OpenClick", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_recorder.IsRecording) StopRecording();
        if (_engine.IsRunning) _engine.Stop();

        _engineMode = EngineMode.Background;
        _engine.Start(BuildClickSettingsFromTab1(), new MultiTarget(targets));
        UpdateEngineButtons();
        UpdateStatusLabel();
    }

    private void StopEngine()
    {
        _engine.Stop();
        _engineMode = EngineMode.None;
        UpdateEngineButtons();
        UpdateStatusLabel();
    }

    private void StartRecording()
    {
        if (_player.IsPlaying) StopPlayback();

        _recorder.IgnoreKeys = BuildIgnoreKeysForRecordHotkey();
        _recorder.Start();
        UpdateRecordButton();
        UpdateStatusLabel();
    }

    private void StopRecording()
    {
        _currentScript = _recorder.StopAndGet();
        _lblEventCount.Text = $"{_currentScript.Events.Count} events recorded";
        _lblMacroInfo.Text = $"{_currentScript.Events.Count} events recorded";
        UpdateRecordButton();
        UpdatePlayButtonEnabled();
        UpdateStatusLabel();
    }

    private void StartPlayback()
    {
        if (_recorder.IsRecording) StopRecording();
        if (_currentScript == null || _currentScript.Events.Count == 0) return;

        double speed = ParseSpeed();
        int repeat = (int)_numRepeatPlayback.Value;
        _player.Start(_currentScript, speed, repeat);
    }

    private void StopPlayback() => _player.Stop();

    // ================================================================
    // UI state helpers
    // ================================================================

    private void UpdateEngineButtons()
    {
        bool holdModeOn = _chkHoldMode.Checked;
        bool running = _engine.IsRunning;

        _btnStart.Enabled = !holdModeOn && !(running && _engineMode == EngineMode.Foreground);
        _btnStop.Enabled = !holdModeOn && running && _engineMode == EngineMode.Foreground;

        _btnStartBackground.Enabled = !(running && _engineMode == EngineMode.Background);
        _btnStopBackground.Enabled = running && _engineMode == EngineMode.Background;
    }

    private void UpdateRecordButton()
    {
        _btnRecordToggle.Text = RecordButtonLabel();
    }

    private void UpdatePlayButtonEnabled()
    {
        bool hasScript = _currentScript != null && _currentScript.Events.Count > 0;
        _btnPlayToggle.Enabled = hasScript || _player.IsPlaying;
        _btnPlayToggle.Text = PlayButtonLabel();
    }

    private void UpdateStatusLabel()
    {
        if (_recorder.IsRecording)
        {
            // Left as-is; EventCaptured keeps this current while recording.
            return;
        }

        if (_player.IsPlaying)
        {
            _lblState.Text = "Playing…";
        }
        else if (_engine.IsRunning)
        {
            _lblState.Text = $"Clicking… ({_engine.ClicksPerformed})";
        }
        else
        {
            _lblState.Text = "Idle";
        }
    }

    private void UpdateButtonLabels()
    {
        _btnStart.Text = $"Start ({_settings.ToggleClickerHotkey.ToDisplayString()})";
        _btnStop.Text = $"Stop ({_settings.ToggleClickerHotkey.ToDisplayString()})";
        _btnRecordToggle.Text = RecordButtonLabel();
        _btnPlayToggle.Text = PlayButtonLabel();
    }

    private string RecordButtonLabel() => _recorder.IsRecording
        ? $"■ Stop Recording ({_settings.ToggleRecordHotkey.ToDisplayString()})"
        : $"● Record ({_settings.ToggleRecordHotkey.ToDisplayString()})";

    private string PlayButtonLabel() => _player.IsPlaying
        ? $"■ Stop ({_settings.TogglePlaybackHotkey.ToDisplayString()})"
        : $"▶ Play ({_settings.TogglePlaybackHotkey.ToDisplayString()})";

    private void UpdateStatusHotkeyLabel()
    {
        _lblHotkeyReminder.Text =
            $"{_settings.ToggleClickerHotkey.ToDisplayString()} start/stop   " +
            $"{_settings.ToggleRecordHotkey.ToDisplayString()} record   " +
            $"{_settings.TogglePlaybackHotkey.ToDisplayString()} play";
    }

    private void RegisterHotkeysFromSettings()
    {
        RegisterOrUnregister("toggle-clicker", _settings.ToggleClickerHotkey);
        RegisterOrUnregister("toggle-record", _settings.ToggleRecordHotkey);
        RegisterOrUnregister("toggle-playback", _settings.TogglePlaybackHotkey);
    }

    private void RegisterOrUnregister(string actionId, HotkeyCombo combo)
    {
        if (combo.IsEmpty)
        {
            _hotkeyManager.Unregister(actionId);
        }
        else
        {
            _hotkeyManager.Register(actionId, combo);
        }
    }

    // ================================================================
    // Settings <-> controls
    // ================================================================

    private void ApplySettingsToControls(AppSettings s)
    {
        _numHours.Value = Math.Clamp(s.Click.IntervalHours, 0, 23);
        _numMinutes.Value = Math.Clamp(s.Click.IntervalMinutes, 0, 59);
        _numSeconds.Value = Math.Clamp(s.Click.IntervalSeconds, 0, 59);
        _numMs.Value = Math.Clamp(s.Click.IntervalMilliseconds, 0, 9999);
        _chkRandomOffset.Checked = s.Click.RandomOffsetEnabled;
        _numRandomOffsetMs.Value = Math.Clamp(s.Click.RandomOffsetMs, 0, 60000);

        _cboButton.SelectedIndex = (int)s.Click.Button;
        _cboClickType.SelectedIndex = (int)s.Click.ClickType;

        if (s.Click.RepeatMode == RepeatMode.Count) _radRepeatCount.Checked = true; else _radRepeatUntilStopped.Checked = true;
        _numRepeatCount.Value = Math.Clamp(s.Click.RepeatCount, 1, 1000000);

        if (s.Click.PositionMode == PositionMode.Fixed) _radPosFixed.Checked = true; else _radPosCurrent.Checked = true;
        _numFixedX.Value = Math.Clamp(s.Click.FixedX, -32768, 32767);
        _numFixedY.Value = Math.Clamp(s.Click.FixedY, -32768, 32767);

        _chkAlwaysOnTop.Checked = s.AlwaysOnTop;
        TopMost = s.AlwaysOnTop;

        _hkClicker.Combo = s.ToggleClickerHotkey;
        _hkRecord.Combo = s.ToggleRecordHotkey;
        _hkPlayback.Combo = s.TogglePlaybackHotkey;

        SetSpeedComboFromValue(s.PlaybackSpeed);
        _numRepeatPlayback.Value = Math.Clamp(s.PlaybackRepeatCount, 0, 10000);

        _lvTargets.Items.Clear();
        foreach (WindowTargetInfo target in s.BackgroundTargets)
        {
            AddTargetToList(target);
        }

        // Hold mode is applied last since its CheckedChanged handler reads the trigger button.
        _holdMonitor.TriggerButton = GetSelectedMouseButton();
        _chkHoldMode.Checked = s.HoldClickMode;
        _holdMonitor.Enabled = s.HoldClickMode;
    }

    private void SaveControlsToSettings()
    {
        _settings.Click = BuildClickSettingsFromTab1();
        _settings.HoldClickMode = _chkHoldMode.Checked;
        _settings.AlwaysOnTop = _chkAlwaysOnTop.Checked;
        _settings.PlaybackSpeed = ParseSpeed();
        _settings.PlaybackRepeatCount = (int)_numRepeatPlayback.Value;
        _settings.BackgroundTargets = GetBackgroundTargetInfos();
    }

    private ClickSettings BuildClickSettingsFromTab1() => new()
    {
        IntervalHours = (int)_numHours.Value,
        IntervalMinutes = (int)_numMinutes.Value,
        IntervalSeconds = (int)_numSeconds.Value,
        IntervalMilliseconds = (int)_numMs.Value,
        RandomOffsetEnabled = _chkRandomOffset.Checked,
        RandomOffsetMs = (int)_numRandomOffsetMs.Value,
        Button = GetSelectedMouseButton(),
        ClickType = _cboClickType.SelectedIndex == 1 ? ClickType.Double : ClickType.Single,
        RepeatMode = _radRepeatCount.Checked ? RepeatMode.Count : RepeatMode.UntilStopped,
        RepeatCount = (int)_numRepeatCount.Value,
        PositionMode = _radPosFixed.Checked ? PositionMode.Fixed : PositionMode.Current,
        FixedX = (int)_numFixedX.Value,
        FixedY = (int)_numFixedY.Value,
    };

    private MouseButton GetSelectedMouseButton() => _cboButton.SelectedIndex switch
    {
        1 => MouseButton.Right,
        2 => MouseButton.Middle,
        _ => MouseButton.Left,
    };

    private List<ushort> BuildIgnoreKeysForRecordHotkey()
    {
        HotkeyCombo combo = _settings.ToggleRecordHotkey;
        List<ushort> list = new();
        if (combo.KeyCode != 0) list.Add((ushort)combo.KeyCode);
        if (combo.Ctrl) list.Add(0x11);
        if (combo.Shift) list.Add(0x10);
        if (combo.Alt) list.Add(0x12);
        if (combo.Win)
        {
            list.Add(0x5B);
            list.Add(0x5C);
        }

        return list;
    }

    private void SetSpeedComboFromValue(double value)
    {
        for (int i = 0; i < _cboSpeed.Items.Count; i++)
        {
            string text = (string)_cboSpeed.Items[i]!;
            if (double.TryParse(text.TrimEnd('x'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double parsed) &&
                Math.Abs(parsed - value) < 0.001)
            {
                _cboSpeed.SelectedIndex = i;
                return;
            }
        }

        _cboSpeed.SelectedIndex = 2; // 1x
    }

    private double ParseSpeed()
    {
        string text = (_cboSpeed.SelectedItem as string) ?? _cboSpeed.Text;
        text = text.TrimEnd('x', 'X');
        return double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v)
            ? v
            : 1.0;
    }

    private void AddTargetToList(WindowTargetInfo info)
    {
        ListViewItem item = new(string.IsNullOrEmpty(info.WindowTitle) ? "(untitled)" : info.WindowTitle)
        {
            Checked = info.Enabled,
            Tag = info,
        };
        item.SubItems.Add(info.ClassName);
        item.SubItems.Add($"({info.ClientX}, {info.ClientY})");
        _lvTargets.Items.Add(item);
    }

    private List<WindowTargetInfo> GetBackgroundTargetInfos()
    {
        List<WindowTargetInfo> list = new();
        foreach (ListViewItem item in _lvTargets.Items)
        {
            if (item.Tag is WindowTargetInfo info)
            {
                list.Add(info);
            }
        }

        return list;
    }

    // ================================================================
    // UI-thread marshaling
    // ================================================================

    private void UiInvoke(Action action)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke(action);
        }
        catch (ObjectDisposedException)
        {
            // Handle was destroyed between the check and the call; ignore.
        }
        catch (InvalidOperationException)
        {
            // Handle not yet created / being torn down; ignore.
        }
    }

    // ================================================================
    // Shutdown
    // ================================================================

    private void OnMainFormClosing(object? sender, FormClosingEventArgs e)
    {
        SaveControlsToSettings();
        SettingsStore.Save(_settings);
    }

    private void OnMainFormClosed(object? sender, FormClosedEventArgs e)
    {
        _engine.Dispose();
        _player.Dispose();
        _recorder.Dispose();
        _hotkeyManager.Dispose();
        _holdMonitor.Dispose();
    }
}
