using System.IO;
using System.Text.Json;

namespace OnRecord;

/// <summary>User settings, stored as JSON in %LOCALAPPDATA%\GlobalMayhem\OnRecord\settings.json.</summary>
public sealed class AppSettings
{
    // Window bounds in DIPs (the normal, non-expanded size). NaN = never saved -> centre on screen.
    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
    public double Width { get; set; } = 508;
    public double Height { get; set; } = 688;
    public bool Expanded { get; set; }

    public bool PinOnTop { get; set; }
    public bool StartWithWindows { get; set; } = true;

    /// <summary>Claude model passed as --model ("" = the CLI's default). Applies to the next new session.</summary>
    public string Model { get; set; } = "";

    /// <summary>Mode name -> "#RRGGBB". Empty = built-in defaults (see Modes.Defaults).</summary>
    public Dictionary<string, string> ModeColors { get; set; } = new();

    /// <summary>
    /// Opt-in: let Claude read and search files anywhere on the PC without asking. Off by default: then it can
    /// only read inside its workspace folder (plus the web). Applies from the next new chat.
    /// </summary>
    public bool AllowReadAnywhere { get; set; }

    /// <summary>Shortcut that opens the popup (one of Platform.Hotkeys.Choices).</summary>
    public string Hotkey { get; set; } = Platform.Hotkeys.Default;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    /// <summary>Set by Load if the file was damaged or had bad values (shown once as a notification).</summary>
    public static string? LoadProblem { get; private set; }

    public static AppSettings Load()
    {
        LoadProblem = null;
        if (!File.Exists(AppPaths.Settings)) return new AppSettings();
        AppSettings? s = null;
        try
        {
            s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.Settings), Json);
            if (s == null) throw new JsonException("empty");
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            string kept = "";
            try
            {
                string backup = AppPaths.Settings + $".damaged-{DateTime.Now:yyyyMMdd-HHmmss}";
                File.Copy(AppPaths.Settings, backup, overwrite: true);
                kept = $" The old file was kept as {Path.GetFileName(backup)}.";
            }
            catch { }
            LoadProblem = "Your settings file couldn't be read, so OnRecord started with default settings." + kept;
            return new AppSettings();
        }
        if (s.Normalize()) LoadProblem = "Some settings had invalid values and were reset to their defaults.";
        return s;
    }

    /// <summary>Repairs hand-edited values. Returns true if anything had to be fixed.</summary>
    private bool Normalize()
    {
        bool fixedAny = false;
        var d = new AppSettings();
        if (!double.IsFinite(Width) || Width < 200 || Width > 20000) { Width = d.Width; fixedAny = true; }
        if (!double.IsFinite(Height) || Height < 200 || Height > 20000) { Height = d.Height; fixedAny = true; }
        if (double.IsInfinity(Left) || double.IsInfinity(Top) || Math.Abs(Left) > 100000 || Math.Abs(Top) > 100000)
        { Left = double.NaN; Top = double.NaN; fixedAny = true; }
        if (Model == null || (Model.Length > 0 && !Claude.ClaudeCli.IsSafeModel(Model))) { Model = ""; fixedAny = true; }
        if (!Platform.Hotkeys.IsKnown(Hotkey)) { Hotkey = Platform.Hotkeys.Default; fixedAny = true; }
        if (ModeColors == null) { ModeColors = new(); fixedAny = true; }
        else
        {
            var bad = ModeColors.Where(kv => kv.Key == null || !Modes.IsValidName(kv.Key) || !Modes.TryParseHex(kv.Value, out _))
                                .Select(kv => kv.Key).ToList();
            foreach (var k in bad) { ModeColors.Remove(k); fixedAny = true; }
        }
        return fixedAny;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Root);
            var tmp = AppPaths.Settings + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, AppPaths.Settings, overwrite: true);
        }
        catch { /* never crash the app over settings */ }
    }
}
