using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenClick.Core;

public enum MouseButton { Left, Right, Middle }
public enum ClickType { Single, Double }
public enum RepeatMode { UntilStopped, Count }
public enum PositionMode { Current, Fixed }
public enum MacroEventType { MouseMove, MouseDown, MouseUp, MouseWheel, KeyDown, KeyUp }

public sealed class ClickSettings
{
    public int IntervalHours { get; set; }          // >= 0
    public int IntervalMinutes { get; set; }        // >= 0
    public int IntervalSeconds { get; set; }        // >= 0
    public int IntervalMilliseconds { get; set; } = 100;
    public bool RandomOffsetEnabled { get; set; }
    public int RandomOffsetMs { get; set; } = 20;   // +/- range
    public MouseButton Button { get; set; } = MouseButton.Left;
    public ClickType ClickType { get; set; } = ClickType.Single;
    public RepeatMode RepeatMode { get; set; } = RepeatMode.UntilStopped;
    public int RepeatCount { get; set; } = 100;     // used when RepeatMode.Count
    public PositionMode PositionMode { get; set; } = PositionMode.Current;
    public int FixedX { get; set; }
    public int FixedY { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public int TotalIntervalMs => Math.Max(1,
        ((IntervalHours * 60 + IntervalMinutes) * 60 + IntervalSeconds) * 1000 + IntervalMilliseconds);

    /// Base interval +/- random offset (uniform in [-RandomOffsetMs, +RandomOffsetMs]), clamped >= 1.
    public int GetEffectiveIntervalMs(Random rng) =>
        RandomOffsetEnabled
            ? Math.Max(1, TotalIntervalMs + rng.Next(-RandomOffsetMs, RandomOffsetMs + 1))
            : TotalIntervalMs;

    public ClickSettings Clone() => (ClickSettings)MemberwiseClone();
}

public sealed class HotkeyCombo
{
    public int KeyCode { get; set; }   // virtual-key code of the main key (e.g., 0x75 = F6)
    public bool Ctrl { get; set; }
    public bool Shift { get; set; }
    public bool Alt { get; set; }
    public bool Win { get; set; }

    [JsonIgnore]
    public bool IsEmpty => KeyCode == 0;

    public string ToDisplayString()
    {
        if (KeyCode == 0)
        {
            return "None";
        }

        var sb = new StringBuilder();
        if (Ctrl) sb.Append("Ctrl+");
        if (Shift) sb.Append("Shift+");
        if (Alt) sb.Append("Alt+");
        if (Win) sb.Append("Win+");
        sb.Append(((System.Windows.Forms.Keys)KeyCode).ToString());
        return sb.ToString();
    }
}

public sealed class WindowTargetInfo
{
    public long HwndValue { get; set; }            // window handle as long
    public string WindowTitle { get; set; } = "";
    public string ClassName { get; set; } = "";
    public int ClientX { get; set; }               // click point in target's CLIENT coords
    public int ClientY { get; set; }
    public bool Enabled { get; set; } = true;

    [System.Text.Json.Serialization.JsonIgnore]
    public IntPtr Hwnd => (IntPtr)HwndValue;

    public string ToDisplayString()
    {
        string title = string.IsNullOrEmpty(WindowTitle)
            ? "(untitled)"
            : WindowTitle.Length > 40 ? WindowTitle[..40] + "…" : WindowTitle;
        return $"{title} [{ClassName}] @ ({ClientX}, {ClientY})";
    }
}

public sealed class MacroEvent
{
    public long TimeMs { get; set; }               // ms since recording start
    public MacroEventType Type { get; set; }
    public int X { get; set; }                     // screen coords (mouse events)
    public int Y { get; set; }
    public MouseButton Button { get; set; }        // mouse down/up only
    public int WheelDelta { get; set; }            // wheel only
    public ushort KeyCode { get; set; }            // key events only (VK)
}

public sealed class MacroScript
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public int Version { get; set; } = 1;
    public string Name { get; set; } = "";
    public List<MacroEvent> Events { get; set; } = new();

    public void Save(string path)
    {
        string json = JsonSerializer.Serialize(this, SerializerOptions);
        File.WriteAllText(path, json);
    }

    public static MacroScript Load(string path)
    {
        string json = File.ReadAllText(path);
        MacroScript? script = JsonSerializer.Deserialize<MacroScript>(json);
        return script ?? throw new InvalidDataException($"Invalid macro file: {path}");
    }
}

public sealed class AppSettings
{
    public ClickSettings Click { get; set; } = new();
    public HotkeyCombo ToggleClickerHotkey { get; set; } = new() { KeyCode = 0x75 };  // F6
    public HotkeyCombo ToggleRecordHotkey { get; set; } = new() { KeyCode = 0x76 };   // F7
    public HotkeyCombo TogglePlaybackHotkey { get; set; } = new() { KeyCode = 0x77 }; // F8
    public bool HoldClickMode { get; set; }
    public bool AlwaysOnTop { get; set; }
    public double PlaybackSpeed { get; set; } = 1.0;
    public int PlaybackRepeatCount { get; set; } = 1;      // 0 = infinite
    public List<WindowTargetInfo> BackgroundTargets { get; set; } = new();
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OpenClick", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            string path = SettingsPath;
            if (!File.Exists(path))
            {
                return new AppSettings();
            }

            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            string path = SettingsPath;
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(path, JsonSerializer.Serialize(settings, SerializerOptions));
        }
        catch
        {
            // Swallow IO errors per spec.
        }
    }
}
