using System.IO;
using System.Windows;

namespace OnRecord.Chat;

public enum PreviewKind { None, Image, Pdf, Text }

/// <summary>
/// Paths from "OPEN: path" lines (Claude wrote them, so they are untrusted) and what the user can do with them.
/// Only full local paths (drive letter) that exist get a card. Nothing is ever opened without a click, and
/// programs and scripts ask first.
/// </summary>
public static class FileTargets
{
    /// <summary>Opening these runs something (or can point anywhere), so "Open" asks first.</summary>
    private static readonly HashSet<string> ProgramExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".com", ".scr", ".pif", ".cpl", ".msc", ".bat", ".cmd", ".ps1", ".psm1", ".psd1", ".ps1xml",
        ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".ws", ".hta", ".msi", ".msp", ".mst", ".msix", ".msixbundle",
        ".appx", ".appxbundle", ".appinstaller", ".application", ".appref-ms", ".lnk", ".url", ".website", ".reg",
        ".inf", ".scf", ".jar", ".py", ".pyw", ".sh", ".gadget", ".settingcontent-ms", ".library-ms", ".search-ms",
        ".diagcab", ".chm", ".sys", ".dll", ".ocx",
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".png", ".jpg", ".jpeg", ".jfif", ".gif", ".bmp", ".tif", ".tiff", ".ico", ".webp", ".heic", ".heif", ".avif", ".jxr", ".wdp" };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".log", ".csv", ".tsv", ".json", ".jsonc", ".xml", ".yaml", ".yml", ".toml", ".ini",
        ".cfg", ".conf", ".config", ".properties", ".env", ".gitignore", ".gitattributes", ".editorconfig", ".sql",
        ".cs", ".csproj", ".sln", ".slnx", ".xaml", ".py", ".js", ".mjs", ".cjs", ".ts", ".tsx", ".jsx", ".java", ".kt", ".c", ".h",
        ".cpp", ".hpp", ".go", ".rs", ".rb", ".php", ".lua", ".swift", ".html", ".htm", ".css", ".scss", ".ps1", ".psm1",
        ".bat", ".cmd", ".sh", ".iss", ".srt", ".vtt", ".nfo", ".rtf",
    };

    public const long MaxTextPreviewBytes = 1024 * 1024;

    /// <summary>
    /// The full path for an "OPEN:" value, or null with a reason. Accepts only a fully qualified local path with a
    /// drive letter (no UNC/network shares, no \\?\ or \\.\ device paths, no alternate data streams).
    /// </summary>
    public static string? Resolve(string? raw, out string? problem)
    {
        problem = null;
        string p = (raw ?? "").Trim().Trim('`', '"', '\'', '<', '>', '*').Trim();
        if (p.Contains('%')) p = Environment.ExpandEnvironmentVariables(p);
        if (p.Length == 0) return Fail("there is no path", out problem);
        if (p.Length > 1000) return Fail("the path is too long", out problem);
        if (p.Any(char.IsControl) || p.IndexOfAny(['"', '<', '>', '|', '?', '*']) >= 0) return Fail("the path has characters Windows doesn't allow", out problem);
        if (p.StartsWith(@"\\") || p.StartsWith("//")) return Fail("network and device paths aren't shown", out problem);
        if (!IsDriveRooted(p)) return Fail("it isn't a full path like C:\\folder\\file", out problem);
        if (p.IndexOf(':', 2) >= 0) return Fail("the path has characters Windows doesn't allow", out problem);
        string full;
        try { full = Path.GetFullPath(p); }
        catch { return Fail("it isn't a valid path", out problem); }
        if (!IsDriveRooted(full) || full.IndexOf(':', 2) >= 0) return Fail("it isn't a normal file path", out problem); // e.g. C:\x\CON -> \\.\CON
        if (full.Length > 3) full = full.TrimEnd('\\');
        return full;
    }

    private static bool IsDriveRooted(string p) =>
        p.Length >= 3 && char.IsAsciiLetter(p[0]) && p[1] == ':' && (p[2] == '\\' || p[2] == '/');

    private static string? Fail(string why, out string? problem) { problem = why; return null; }

    public static bool Exists(string path, out bool isFolder)
    {
        isFolder = false;
        try
        {
            if (Directory.Exists(path)) { isFolder = true; return true; }
            return File.Exists(path);
        }
        catch { return false; }
    }

    public static bool IsProgram(string path) => ProgramExtensions.Contains(Path.GetExtension(path));

    public static PreviewKind KindOf(string path)
    {
        string ext = Path.GetExtension(path);
        if (ImageExtensions.Contains(ext)) return PreviewKind.Image;
        if (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase)) return PreviewKind.Pdf;
        if (TextExtensions.Contains(ext) && !ext.Equals(".rtf", StringComparison.OrdinalIgnoreCase)) return PreviewKind.Text;
        return PreviewKind.None;
    }

    private static string Explorer => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

    private static bool TestMode => Environment.GetEnvironmentVariable("ONRECORD_TEST_NO_EXTERNAL") == "1";

    private static void Trace(string line)
    {
        try { File.AppendAllText(Path.Combine(AppPaths.Logs, "open-trace.log"), $"{DateTime.Now:HH:mm:ss} {line}\n"); } catch { }
    }

    /// <summary>
    /// Opens the file in its default program, or the folder in File Explorer. Programs and scripts ask first.
    /// Returns an error message to show (e.g. the file was deleted), or null.
    /// </summary>
    public static string? Open(string path, Window? owner)
    {
        if (!Exists(path, out bool isFolder))
            return "It isn't there any more. It may have been moved, renamed or deleted.";
        if (!isFolder && IsProgram(path))
        {
            bool go = Dialogs.Confirm(owner, "Open program", $"Run {Path.GetFileName(path)}?",
                $"This is a program, script or shortcut, so opening it runs it:\n{path}\n\n" +
                "Only run it if you know what it is and trust where it came from.", "Run it");
            if (!go) return null;
            if (!Exists(path, out _)) return "It isn't there any more. It may have been moved, renamed or deleted.";
        }
        try
        {
            if (TestMode) { Trace($"open {path}"); return null; }
            if (isFolder)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Explorer) { ArgumentList = { path }, UseShellExecute = false });
            else
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path) });
            return null;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1155) // no program associated
        {
            return "Windows has no program set up to open this kind of file. Use \"Show in File Explorer\" and pick one with Open with.";
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { return null; } // cancelled (e.g. UAC)
        catch (Exception ex) { return $"Couldn't open it: {ex.Message}"; }
    }

    /// <summary>Opens File Explorer with the file or folder selected. Returns an error message or null.</summary>
    public static string? ShowInExplorer(string path)
    {
        if (!Exists(path, out _))
            return "It isn't there any more. It may have been moved, renamed or deleted.";
        try
        {
            if (TestMode) { Trace($"select {path}"); return null; }
            // A validated drive path never contains a quote, so this argument can't be broken out of.
            System.Diagnostics.Process.Start(Explorer, $"/select,\"{path}\"");
            return null;
        }
        catch (Exception ex) { return $"Couldn't open File Explorer: {ex.Message}"; }
    }

    public static string FormatSize(long bytes)
    {
        string[] units = ["bytes", "KB", "MB", "GB", "TB"];
        double v = bytes; int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return u == 0 ? $"{bytes:N0} bytes" : $"{v:0.#} {units[u]}";
    }
}
