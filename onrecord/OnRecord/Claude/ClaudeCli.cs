using System.IO;
using System.Text.RegularExpressions;

namespace OnRecord.Claude;

/// <summary>Finds the official `claude` CLI and builds the arguments for an OnRecord session.</summary>
public static partial class ClaudeCli
{
    /// <summary>
    /// Tools Claude may use. Shell tools (Bash/PowerShell) are deliberately absent: running commands is
    /// blocked. Changing this list is a product decision.
    /// </summary>
    public static readonly string[] Tools =
        ["Read", "Glob", "Grep", "Edit", "Write", "NotebookEdit", "WebSearch", "WebFetch", "ToolSearch"];

    /// <summary>
    /// Always auto-approved: the web. Reading inside the workspace (the working directory) is allowed by the
    /// CLI itself; Edit/Write are approved only inside the workspace via acceptEdits mode.
    /// </summary>
    private static readonly string[] AlwaysAllowed = ["WebSearch", "WebFetch", "ToolSearch"];

    /// <summary>Only when the user opted in (Settings): read/search files anywhere on the PC.</summary>
    private static readonly string[] ReadAnywhere = ["Read", "Glob", "Grep"];

    /// <summary>Bigger rules than this are ignored (Windows limits a command line to ~32k characters).</summary>
    public const int MaxRulesLength = 20_000;

    // ---------------------------------------------------------------- finding the CLI

    public readonly record struct Launch(string FileName, string[] PrefixArgs);

    /// <summary>How to start claude, never through cmd.exe (which would re-parse our arguments). Null if not found.</summary>
    public static Launch? FindLaunch()
    {
        var dirs = new List<string>();
        foreach (var scope in new[] { EnvironmentVariableTarget.Process, EnvironmentVariableTarget.User, EnvironmentVariableTarget.Machine })
            dirs.AddRange((Environment.GetEnvironmentVariable("PATH", scope) ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries));
        dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin"));

        foreach (var name in new[] { "claude.exe", "claude.cmd" })
            foreach (var raw in dirs)
            {
                try
                {
                    var dir = Environment.ExpandEnvironmentVariables(raw.Trim().Trim('"'));
                    if (!Path.IsPathFullyQualified(dir)) continue; // never relative PATH entries like "." (planting)
                    var path = Path.Combine(dir, name);
                    if (File.Exists(path) && ResolveLaunch(path) is { } launch) return launch;
                }
                catch { /* bad PATH entry */ }
            }
        return null;
    }

    /// <summary>
    /// claude.exe runs directly. An npm install's claude.cmd is NOT run (cmd.exe would interpret characters like
    /// &amp; | % in our arguments); instead its node script is run with node.exe, which gets the arguments verbatim.
    /// </summary>
    public static Launch? ResolveLaunch(string path)
    {
        if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return new Launch(path, []);
        if (!path.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)) return null;

        string dir = Path.GetDirectoryName(path)!;
        string cli = Path.Combine(dir, "node_modules", "@anthropic-ai", "claude-code", "cli.js");
        if (!File.Exists(cli)) return null;
        string node = Path.Combine(dir, "node.exe");
        if (!File.Exists(node))
        {
            node = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(d => d.Trim().Trim('"'))
                .Where(Path.IsPathFullyQualified)
                .Select(d => Path.Combine(d, "node.exe"))
                .FirstOrDefault(File.Exists) ?? "";
            if (node.Length == 0) return null;
        }
        return new Launch(node, [cli]);
    }

    // ---------------------------------------------------------------- rules

    /// <summary>
    /// The user's rules.txt with {ARTIFACTS_DIR} filled in. Falls back to the default rules if the file can't be
    /// used, and reports why in <paramref name="problem"/> (shown in the chat) so the app keeps working.
    /// </summary>
    public static string LoadRules(out string? problem)
    {
        problem = null;
        string? text = RulesFile.ReadUser();
        if (text == null)
        {
            problem = "Couldn't read rules.txt, so OnRecord is using the default rules for now.";
            text = RulesFile.ReadBundled();
        }
        else if (text.Length > MaxRulesLength)
        {
            problem = $"rules.txt is too long (over {MaxRulesLength:N0} characters), so OnRecord is using the default rules. Open Settings to restore or shorten it.";
            text = RulesFile.ReadBundled();
        }
        else if (string.IsNullOrWhiteSpace(text))
        {
            problem = "rules.txt is empty, so OnRecord is using the default rules. Use \"Restore default rules\" in Settings to fix it.";
            text = RulesFile.ReadBundled();
        }
        else
        {
            var missing = new List<string>();
            if (RulesFile.ParseTints(text).Count == 0) missing.Add("colour changes (tint tags)");
            if (!text.Contains("ARTIFACT:", StringComparison.Ordinal)) missing.Add("artifact previews");
            if (missing.Count > 0)
                problem = $"rules.txt has been edited and {string.Join(" and ", missing)} may not work. Use \"Restore default rules\" in Settings to fix it.";
        }
        return text.Replace("{ARTIFACTS_DIR}", AppPaths.Artifacts);
    }

    // ---------------------------------------------------------------- arguments

    /// <summary>
    /// The argument list (passed through ProcessStartInfo.ArgumentList, never one command string).
    /// Values that come from files the user can edit (rules, model) are passed as "--flag=value", so even a
    /// value starting with "--" can never be read as an extra flag.
    /// </summary>
    public static List<string> BuildArguments(string rules, string? model, bool allowReadAnywhere)
    {
        var allowed = allowReadAnywhere ? ReadAnywhere.Concat(AlwaysAllowed) : AlwaysAllowed;
        var args = new List<string>
        {
            "-p",
            "--input-format", "stream-json",
            "--output-format", "stream-json",
            "--verbose",
            "--include-partial-messages",
            "--permission-mode", "acceptEdits",
            "--tools", string.Join(",", Tools),
            "--allowedTools", string.Join(",", allowed),
            // Don't inherit allow-rules or MCP servers from the user's other Claude Code setups.
            "--setting-sources", "",
            "--strict-mcp-config",
        };
        if (!string.IsNullOrWhiteSpace(rules))
            args.Add("--append-system-prompt=" + rules);
        if (!string.IsNullOrWhiteSpace(model) && IsSafeModel(model))
            args.Add("--model=" + model);
        return args;
    }

    /// <summary>Model names like "opus", "claude-sonnet-5-5", "sonnet[1m]", "us.anthropic.x:0".</summary>
    public static bool IsSafeModel(string? model) =>
        !string.IsNullOrEmpty(model) && model.Length <= 100 && char.IsAsciiLetterOrDigit(model[0])
        && model.All(ch => char.IsAsciiLetterOrDigit(ch) || ".-_:[]/".Contains(ch));

    // ---------------------------------------------------------------- never leak secrets

    [GeneratedRegex(@"sk-ant-[A-Za-z0-9_\-]+")]
    private static partial Regex AnthropicKey();
    [GeneratedRegex(@"(?i)(bearer\s+)[A-Za-z0-9._\-~+/=]+")]
    private static partial Regex Bearer();
    [GeneratedRegex(@"(?i)(""(?:access_?token|refresh_?token|api_?key|authorization|oauth_?token)""\s*:\s*"")[^""]*")]
    private static partial Regex JsonSecret();

    /// <summary>Removes anything that looks like a key or token before text is logged or shown.</summary>
    public static string Redact(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        text = AnthropicKey().Replace(text, "[redacted]");
        text = Bearer().Replace(text, "$1[redacted]");
        text = JsonSecret().Replace(text, "$1[redacted]");
        return text;
    }
}
