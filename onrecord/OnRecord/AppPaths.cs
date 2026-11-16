using System.IO;

namespace OnRecord;

/// <summary>All runtime locations under %LOCALAPPDATA%\GlobalMayhem\OnRecord. Never inside the code folder.</summary>
public static class AppPaths
{
    public const string AppName = "OnRecord";
    public const string Publisher = "Global Mayhem";

    private static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public static string Root { get; } = Path.Combine(LocalAppData, "GlobalMayhem", "OnRecord");

    public static string Settings => Path.Combine(Root, "settings.json");
    public static string Rules => Path.Combine(Root, "rules.txt");
    public static string Workspace => Path.Combine(Root, "workspace");
    public static string Artifacts => Path.Combine(Workspace, "artifacts");
    /// <summary>Copies of files the user attaches (originals are never touched).</summary>
    public static string Attachments => Path.Combine(Workspace, "attachments");
    public static string Logs => Path.Combine(Root, "logs");
    /// <summary>WebView2 profile used ONLY by the artifact preview (untrusted pages).</summary>
    public static string PreviewWebViewData => Path.Combine(Root, "preview-webview");
    /// <summary>
    /// Persistent WebView2 profile for web pages from WEB lines (logins, cookies). Never shared with the artifact
    /// preview, and Claude may never read it (see <see cref="Claude.ClaudeCli.BuildArguments"/>).
    /// </summary>
    public static string WebProfile => Path.Combine(Root, "web");

    public static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "OnRecord.exe");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Workspace);
        Directory.CreateDirectory(Artifacts);
        Directory.CreateDirectory(Attachments);
        Directory.CreateDirectory(Logs);
    }

    /// <summary>
    /// One-time move from the old data folder (%LOCALAPPDATA%\Launcher, from before the rename).
    /// Moves everything if the new folder doesn't exist yet; otherwise copies over only what's missing.
    /// Returns a message if something couldn't be moved.
    /// </summary>
    public static string? MigrateFromOldLocation()
    {
        string old = Path.Combine(LocalAppData, "Launcher");
        if (!Directory.Exists(old) || !LooksLikeOurOldData(old)) return null; // another app's "Launcher" folder: leave it alone
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Root)!);
            if (!Directory.Exists(Root))
            {
                Directory.Move(old, Root);
                return null;
            }
            CopyMissing(old, Root);
            Directory.Delete(old, recursive: true);
            return null;
        }
        catch (Exception ex)
        {
            return $"Couldn't fully move your old data from {old}: {ex.Message}";
        }
    }

    /// <summary>Our pre-rename data folder has our rules.txt (with tint tags) and a workspace\artifacts folder.</summary>
    private static bool LooksLikeOurOldData(string dir)
    {
        try
        {
            var rules = Path.Combine(dir, "rules.txt");
            return Directory.Exists(Path.Combine(dir, "workspace", "artifacts"))
                   && File.Exists(rules) && File.ReadAllText(rules).Contains("[tint:", StringComparison.Ordinal);
        }
        catch { return false; }
    }

    /// <summary>Removes the WebView2 profile from before the preview got its own folder (best effort).</summary>
    public static void RemoveOldWebViewData()
    {
        try { var old = Path.Combine(Root, "webview"); if (Directory.Exists(old)) Directory.Delete(old, recursive: true); }
        catch { /* in use or locked: try again next start */ }
    }

    private static void CopyMissing(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from))
        {
            var target = Path.Combine(to, Path.GetFileName(file));
            if (!File.Exists(target)) File.Copy(file, target);
        }
        foreach (var dir in Directory.GetDirectories(from))
            CopyMissing(dir, Path.Combine(to, Path.GetFileName(dir)));
    }
}
