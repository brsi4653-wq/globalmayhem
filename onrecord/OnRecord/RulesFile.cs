using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace OnRecord;

/// <summary>
/// The user's editable rules.txt (sent to Claude as the system-prompt addition).
/// The app only ever rewrites the tint tag list: the contiguous block of "[tint:name] description" lines.
/// Every other line is left exactly as it is.
/// </summary>
public static partial class RulesFile
{
    public const int MaxDescriptionLength = 120;

    /// <summary>The 1.1.0 rule for file cards (also added once to older rules.txt files, see AddMissingRules).</summary>
    public const string OpenRule =
        "When you find files or folders the user wants to see or open, add one line per item, alone: OPEN: <full path>";

    [GeneratedRegex(@"^\s*\[tint:\s*([A-Za-z0-9_\-]+)\s*\]\s?(.*)$")]
    private static partial Regex TintLine();

    /// <summary>The default rules shipped next to the exe.</summary>
    public static string BundledPath => Path.Combine(AppContext.BaseDirectory, "rules.txt");

    /// <summary>Fallback if the bundled file is missing (keeps the app working). Must match the shipped rules.txt.</summary>
    public const string FallbackDefault =
        "You're running inside OnRecord, a popup chat app on the user's Windows PC. The app reads tags in your replies and hides them from the user. Never mention the tags.\n" +
        "\n" +
        "First line of every reply, alone: one tint tag for what you're doing.\n" +
        "[tint:claude] chat, writing, answering\n" +
        "[tint:code] writing or running code, building things\n" +
        "[tint:search] searching the user's PC or files\n" +
        "[tint:web] searching the web\n" +
        "\n" +
        "When you make anything the user should see rendered (a page, tool, visual), save it as one self-contained HTML file in {ARTIFACTS_DIR}, then add this line alone:\n" +
        "ARTIFACT: filename.html\n" +
        "\n" +
        OpenRule + "\n" +
        "\n" +
        "When a message ends with \"Attached files:\" and file paths, the user attached those files: read them with the Read tool (it handles images and PDFs) before answering.\n";

    public static string ReadBundled()
    {
        try { if (File.Exists(BundledPath)) { var t = File.ReadAllText(BundledPath); if (ParseTints(t).Count > 0) return t; } }
        catch { }
        return FallbackDefault;
    }

    /// <summary>Descriptions of the built-in modes, from the default rules.</summary>
    public static Dictionary<string, string> DefaultDescriptions() =>
        ParseTints(ReadBundled()).ToDictionary(t => t.Name, t => t.Description, StringComparer.OrdinalIgnoreCase);

    /// <summary>Makes a description safe to put on one line of rules.txt.</summary>
    public static string Sanitize(string? description)
    {
        string s = description ?? "";
        s = Regex.Replace(s, @"[\r\n\t  \u0085]+", " ");          // one line only
        s = Regex.Replace(s, @"[\x00-\x1F\x7F]", "");                         // other control chars
        s = Regex.Replace(s, @"\[[^\]]*\]", " ");                              // [tag-like] text
        s = s.Replace("[", "").Replace("]", "").Replace("{", "").Replace("}", "");
        s = Regex.Replace(s, @"ARTIFACT\s*:", "", RegexOptions.IgnoreCase);    // artifact tag text
        s = Regex.Replace(s, @"\bOPEN\s*:", "", RegexOptions.IgnoreCase);      // file card tag text
        s = Regex.Replace(s, @"\s{2,}", " ").Trim();
        if (s.Length > MaxDescriptionLength) s = s[..MaxDescriptionLength].TrimEnd();
        return s;
    }

    public sealed record Tint(string Name, string Description);

    /// <summary>All "[tint:name] description" lines in the text (first block only).</summary>
    public static List<Tint> ParseTints(string text)
    {
        var lines = SplitLines(text);
        var (first, last) = FindBlock(lines);
        var list = new List<Tint>();
        if (first < 0) return list;
        for (int i = first; i <= last; i++)
        {
            var m = TintLine().Match(lines[i]);
            list.Add(new Tint(m.Groups[1].Value.ToLowerInvariant(), m.Groups[2].Value.Trim()));
        }
        return list;
    }

    /// <summary>
    /// Replaces only the tint list with one line per mode. If the list was deleted, it is re-inserted after the
    /// "First line of every reply" instruction. Returns null if there is no sensible place for it.
    /// </summary>
    public static string? ReplaceTintList(string text, IEnumerable<Tint> modes)
    {
        string nl = text.Contains("\r\n") ? "\r\n" : "\n";
        var lines = SplitLines(text);
        var newLines = modes.Select(m => FormatLine(m.Name, m.Description)).ToList();
        var (first, last) = FindBlock(lines);
        if (first >= 0)
        {
            lines.RemoveRange(first, last - first + 1);
            lines.InsertRange(first, newLines);
        }
        else
        {
            int anchor = lines.FindIndex(l => l.Contains("tint tag", StringComparison.OrdinalIgnoreCase));
            if (anchor < 0) return null;
            lines.InsertRange(anchor + 1, newLines);
        }
        return string.Join(nl, lines);
    }

    public static string FormatLine(string name, string description)
    {
        string d = Sanitize(description);
        return d.Length == 0 ? $"[tint:{name}]" : $"[tint:{name}] {d}";
    }

    // ---------------------------------------------------------------- the user's copy on disk

    /// <summary>Text of the user's rules.txt, creating it from the defaults first if needed. Null if unreadable.</summary>
    public static string? ReadUser()
    {
        try
        {
            EnsureUserCopy();
            return File.ReadAllText(AppPaths.Rules);
        }
        catch { return null; }
    }

    public static void EnsureUserCopy()
    {
        if (File.Exists(AppPaths.Rules)) return;
        Directory.CreateDirectory(AppPaths.Root);
        WriteUser(ReadBundled());
    }

    /// <summary>
    /// Adds rules that a newer version needs to an older user copy: the OPEN rule goes after the ARTIFACT line
    /// (or at the end). Returns the updated text, or null if nothing needed adding.
    /// </summary>
    public static string? WithMissingRules(string text)
    {
        if (text.Contains("OPEN:", StringComparison.Ordinal)) return null;
        string nl = text.Contains("\r\n") ? "\r\n" : "\n";
        var lines = SplitLines(text);
        int artifact = lines.FindIndex(l => l.TrimStart().StartsWith("ARTIFACT:", StringComparison.Ordinal));
        if (artifact >= 0) lines.InsertRange(artifact + 1, ["", OpenRule]);
        else
        {
            while (lines.Count > 0 && lines[^1].Trim().Length == 0) lines.RemoveAt(lines.Count - 1);
            lines.AddRange(["", OpenRule, ""]);
        }
        return string.Join(nl, lines);
    }

    /// <summary>Applies <see cref="WithMissingRules"/> to the user's rules.txt. Returns an error message or null.</summary>
    public static string? AddMissingRules()
    {
        var text = ReadUser();
        if (text == null || WithMissingRules(text) is not { } updated) return null;
        try { WriteUser(updated); return null; }
        catch (Exception ex) { return $"Couldn't add the new file card rule to rules.txt: {ex.Message}"; }
    }

    /// <summary>Rewrites the tint list in the user's rules.txt. Returns an error message, or null on success.</summary>
    public static string? SyncTintList(IEnumerable<Tint> modes)
    {
        var text = ReadUser();
        if (text == null) return "Couldn't read rules.txt, so Claude won't know about mode changes until it's fixed.";
        var updated = ReplaceTintList(text, modes);
        if (updated == null)
            return "rules.txt no longer has a tint tag list, so Claude won't know about your modes. Use \"Restore default rules\" to fix it.";
        if (updated == text) return null;
        try { WriteUser(updated); return null; }
        catch (Exception ex) { return $"Couldn't save rules.txt: {ex.Message}"; }
    }

    /// <summary>Puts back the default rules, with the tint list for the given modes. Returns an error message or null.</summary>
    public static string? RestoreDefault(IEnumerable<Tint> modes)
    {
        try
        {
            var text = ReplaceTintList(ReadBundled(), modes) ?? ReadBundled();
            WriteUser(text);
            return null;
        }
        catch (Exception ex) { return $"Couldn't restore rules.txt: {ex.Message}"; }
    }

    private static void WriteUser(string text)
    {
        var tmp = AppPaths.Rules + ".tmp";
        File.WriteAllText(tmp, text, new UTF8Encoding(false));
        File.Move(tmp, AppPaths.Rules, overwrite: true);
    }

    // ---------------------------------------------------------------- helpers

    private static List<string> SplitLines(string text) =>
        text.Replace("\r\n", "\n").Split('\n').ToList();

    private static (int first, int last) FindBlock(List<string> lines)
    {
        int first = lines.FindIndex(l => TintLine().IsMatch(l));
        if (first < 0) return (-1, -1);
        int last = first;
        while (last + 1 < lines.Count && TintLine().IsMatch(lines[last + 1])) last++;
        return (first, last);
    }
}
