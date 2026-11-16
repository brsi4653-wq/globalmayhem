using System.Text.RegularExpressions;

namespace OnRecord.Chat;

/// <summary>[tint:mode] on the first line of a reply: switch mode/colour. Unknown modes are hidden but ignored.</summary>
public sealed partial class TintTagHandler(Action<string> onMode) : ITagHandler
{
    private const string Prefix = "[tint:";

    [GeneratedRegex(@"^\s*\[tint:\s*([A-Za-z0-9_\-]+)\s*\]\s*$")]
    private static partial Regex Pattern();

    public bool TryHandle(string line, bool isFirstLine)
    {
        if (!isFirstLine) return false;
        var m = Pattern().Match(line);
        if (!m.Success) return false;
        string mode = m.Groups[1].Value.ToLowerInvariant();
        if (Modes.IsKnown(mode)) onMode(mode); // unknown: keep the current mode
        return true;
    }

    public bool CouldBecomeTag(string partial, bool isFirstLine)
    {
        if (!isFirstLine) return false;
        string t = partial.TrimStart();
        if (t.Length == 0) return true;
        // Hold the line while it is (or may still become) a tint tag, including a complete "[tint:x]"
        // whose newline hasn't arrived yet. It is only released once the line ends and proves not to be a tag.
        return Prefix.StartsWith(t, StringComparison.OrdinalIgnoreCase)
               || t.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>"ARTIFACT: file.html" on any line: hidden; opens the file from the artifacts folder.</summary>
public sealed partial class ArtifactTagHandler(Action<string> onArtifact) : ITagHandler
{
    private const string Prefix = "ARTIFACT:";

    [GeneratedRegex(@"^\s*ARTIFACT:\s*(.+?)\s*$")]
    private static partial Regex Pattern();

    public bool TryHandle(string line, bool isFirstLine)
    {
        var m = Pattern().Match(line);
        if (!m.Success) return false;
        onArtifact(m.Groups[1].Value.Trim().Trim('`', '"', '\''));
        return true;
    }

    public bool CouldBecomeTag(string partial, bool isFirstLine)
    {
        string t = partial.TrimStart();
        if (t.Length == 0) return false;
        return Prefix.StartsWith(t, StringComparison.Ordinal) || t.StartsWith(Prefix, StringComparison.Ordinal);
    }
}

/// <summary>
/// "WEB: url" on its own line: hidden; opens that page in the side panel (replacing an artifact or earlier page).
/// The URL is untrusted text: the caller only ever opens http/https, anything else becomes a search.
/// </summary>
public sealed partial class WebTagHandler(Action<string> onWeb) : ITagHandler
{
    private const string Prefix = "WEB:";

    [GeneratedRegex(@"^\s*WEB:\s*(\S.*?)\s*$")]
    private static partial Regex Pattern();

    public bool TryHandle(string line, bool isFirstLine)
    {
        var m = Pattern().Match(line);
        if (!m.Success) return false;
        onWeb(m.Groups[1].Value);
        return true;
    }

    public bool CouldBecomeTag(string partial, bool isFirstLine)
    {
        string t = partial.TrimStart();
        if (t.Length == 0) return false;
        return Prefix.StartsWith(t, StringComparison.Ordinal) || t.StartsWith(Prefix, StringComparison.Ordinal);
    }
}

/// <summary>
/// "LAUNCH: app name" on its own line: hidden; opens that installed app (matched by name against Windows' Apps
/// folder, never used as a path or command). The caller decides whether the user actually asked to open something.
/// </summary>
public sealed partial class LaunchTagHandler(Action<string> onLaunch) : ITagHandler
{
    private const string Prefix = "LAUNCH:";

    [GeneratedRegex(@"^\s*LAUNCH:\s*(\S.*?)\s*$")]
    private static partial Regex Pattern();

    public bool TryHandle(string line, bool isFirstLine)
    {
        var m = Pattern().Match(line);
        if (!m.Success) return false;
        onLaunch(m.Groups[1].Value.Trim().Trim('`', '"', '\'').Trim());
        return true;
    }

    public bool CouldBecomeTag(string partial, bool isFirstLine)
    {
        string t = partial.TrimStart();
        if (t.Length == 0) return false;
        return Prefix.StartsWith(t, StringComparison.Ordinal) || t.StartsWith(Prefix, StringComparison.Ordinal);
    }
}

/// <summary>"OPEN: C:\full\path" on any line: hidden; shows a file card for that file or folder (never opens it).</summary>
public sealed partial class OpenTagHandler(Action<string> onOpen) : ITagHandler
{
    private const string Prefix = "OPEN:";

    [GeneratedRegex(@"^\s*OPEN:\s*(.+?)\s*$")]
    private static partial Regex Pattern();

    public bool TryHandle(string line, bool isFirstLine)
    {
        var m = Pattern().Match(line);
        if (!m.Success) return false;
        onOpen(m.Groups[1].Value);
        return true;
    }

    public bool CouldBecomeTag(string partial, bool isFirstLine)
    {
        string t = partial.TrimStart();
        if (t.Length == 0) return false;
        return Prefix.StartsWith(t, StringComparison.Ordinal) || t.StartsWith(Prefix, StringComparison.Ordinal);
    }
}
