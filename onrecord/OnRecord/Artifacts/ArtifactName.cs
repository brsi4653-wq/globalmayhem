using System.IO;

namespace OnRecord.Artifacts;

/// <summary>
/// Validates the file name from an "ARTIFACT:" line. Only a plain *.html file directly inside the artifacts
/// folder is ever accepted: no folders, "..", drive letters, absolute/UNC paths, streams or device names.
/// </summary>
public static class ArtifactName
{
    private static readonly string[] DeviceNames =
        ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
         "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"];

    /// <summary>Returns the full path, or null with a reason.</summary>
    public static string? Resolve(string? name, string artifactsDir, out string? problem)
    {
        problem = null;
        string n = (name ?? "").Trim();
        if (n.Length == 0) return Fail("it has no file name", out problem);
        if (n.Length > 120) return Fail("the name is too long", out problem);
        if (n.IndexOfAny(['/', '\\', ':', '*', '?', '"', '<', '>', '|']) >= 0 || n.Any(char.IsControl))
            return Fail("only a plain file name inside the artifacts folder is allowed", out problem);
        if (n.Contains("..") || n.StartsWith('.') || n.EndsWith('.') || n.EndsWith(' '))
            return Fail("only a plain file name inside the artifacts folder is allowed", out problem);
        if (!n.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            return Fail("only .html files can be previewed", out problem);
        string stem = n[..n.IndexOf('.')];
        if (DeviceNames.Contains(stem, StringComparer.OrdinalIgnoreCase))
            return Fail("that name is reserved by Windows", out problem);

        string dir = Path.GetFullPath(artifactsDir).TrimEnd(Path.DirectorySeparatorChar);
        string full = Path.GetFullPath(Path.Combine(dir, n));
        if (!string.Equals(Path.GetDirectoryName(full), dir, StringComparison.OrdinalIgnoreCase))
            return Fail("only a plain file name inside the artifacts folder is allowed", out problem);
        return full;
    }

    /// <summary>The file exists and is a normal file (not a link/junction that could point elsewhere).</summary>
    public static bool IsSafeExistingFile(string fullPath)
    {
        try
        {
            var info = new FileInfo(fullPath);
            return info.Exists && (info.Attributes & FileAttributes.ReparsePoint) == 0;
        }
        catch { return false; }
    }

    private static string? Fail(string why, out string? problem) { problem = why; return null; }
}
