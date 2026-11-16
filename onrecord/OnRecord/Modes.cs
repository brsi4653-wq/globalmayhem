using System.Text.RegularExpressions;
using System.Windows.Media;

namespace OnRecord;

/// <summary>
/// The single place where modes and their tint colours live.
/// Defaults are below; Settings can override/add modes (saved in settings.json as ModeColors).
/// </summary>
public static partial class Modes
{
    public const string Default = "claude";

    /// <summary>Built-in modes. To add a mode in code, add one line here.</summary>
    public static readonly IReadOnlyDictionary<string, string> Defaults = new Dictionary<string, string>
    {
        ["claude"] = "#FF8A3D",
        ["code"] = "#9B6BFF",
        ["search"] = "#3D8BFF",
        ["web"] = "#2EC4A6",
    };

    /// <summary>
    /// Hidden built-in modes: always available to Claude's tint tag, but never listed, edited, removed or reset in
    /// Settings, and never saved in settings.json. Their rules live in <see cref="Claude.BuiltInRules"/>, not in rules.txt.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Hidden = new Dictionary<string, string>
    {
        [EasterEgg] = "#FFC83D",
    };

    /// <summary>The Easter egg mode (gold). Its pill reads "OnRecord".</summary>
    public const string EasterEgg = "easteregg";

    public static bool IsHidden(string? mode) => mode != null && Hidden.ContainsKey(mode.ToLowerInvariant());

    /// <summary>The active map (defaults, or the user's map from Settings) plus the hidden modes.</summary>
    public static readonly Dictionary<string, Color> Colors = new(StringComparer.OrdinalIgnoreCase);

    static Modes() => Load(null);

    /// <summary>Replaces the active map. Invalid entries are skipped; "claude" always exists.</summary>
    public static void Load(IDictionary<string, string>? map)
    {
        Colors.Clear();
        IEnumerable<KeyValuePair<string, string>> source = map is { Count: > 0 } ? map : Defaults;
        foreach (var (name, hex) in source)
            if (IsValidName(name) && !IsHidden(name) && TryParseHex(hex, out var c))
                Colors[name.ToLowerInvariant()] = c;
        if (!Colors.ContainsKey(Default)) Colors[Default] = FromHex(Defaults[Default]);
        foreach (var (name, hex) in Hidden) Colors[name] = FromHex(hex); // users can't override these
    }

    /// <summary>The modes Settings shows and edits (everything except the hidden ones).</summary>
    public static IEnumerable<KeyValuePair<string, Color>> Visible => Colors.Where(kv => !IsHidden(kv.Key));

    public static bool IsKnown(string mode) => Colors.ContainsKey(mode);

    public static Color ColorOf(string mode) =>
        Colors.TryGetValue(mode, out var c) ? c : Colors[Default];

    /// <summary>Display name for the mode pill, e.g. "claude" -> "Claude".</summary>
    public static string DisplayName(string mode) =>
        string.Equals(mode, EasterEgg, StringComparison.OrdinalIgnoreCase) ? AppPaths.AppName :
        string.IsNullOrEmpty(mode) ? mode : char.ToUpperInvariant(mode[0]) + mode[1..].ToLowerInvariant();

    [GeneratedRegex("^[a-z0-9_-]{1,24}$", RegexOptions.IgnoreCase)]
    private static partial Regex NamePattern();

    public static bool IsValidName(string name) => NamePattern().IsMatch(name ?? "");

    [GeneratedRegex("^[a-z0-9-]{1,24}$")]
    private static partial Regex NewNamePattern();

    /// <summary>Names saved from Settings: lowercase letters, numbers and hyphens only.</summary>
    public static bool IsValidNewName(string name) => NewNamePattern().IsMatch(name ?? "");

    public static bool TryParseHex(string? hex, out Color color)
    {
        color = default;
        hex = (hex ?? "").Trim();
        if (!hex.StartsWith('#')) hex = "#" + hex;
        if (hex.Length != 7) return false;
        try
        {
            color = (Color)ColorConverter.ConvertFromString(hex);
            return true;
        }
        catch { return false; }
    }

    public static Color FromHex(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
}
