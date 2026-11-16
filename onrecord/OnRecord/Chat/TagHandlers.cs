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
