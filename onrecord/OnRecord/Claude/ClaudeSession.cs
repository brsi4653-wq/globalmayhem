using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;

namespace OnRecord.Claude;

/// <summary>
/// One persistent `claude -p` process in stream-json mode = one conversation.
/// Messages are written to stdin as JSON lines; events are read from stdout and raised on the UI thread.
/// </summary>
public sealed class ClaudeSession : IDisposable
{
    private readonly Dispatcher _ui;
    private Process? _process;
    private readonly StringBuilder _stderr = new();
    private bool _stopping;
    private volatile bool _textInBlockSeen; // any text emitted in the current turn (to separate later text blocks)
    private bool _inTextBlock;              // reader thread only

    /// <summary>The current assistant text block ended (its last line is complete).</summary>
    public event Action? TextBlockEnded;

    // ---- events (always raised on the UI thread)
    /// <summary>A new assistant text block started (after a tool call, etc.).</summary>
    public event Action? TextBlockStarted;
    public event Action<string>? TextDelta;
    /// <summary>Claude started using a tool: (tool name, short description of what it touches).</summary>
    public event Action<string, string>? ToolStarted;
    public event Action<string>? PermissionDenied;
    /// <summary>A reply finished: (isError, errorText).</summary>
    public event Action<bool, string?>? TurnCompleted;
    /// <summary>The process exited without Stop() being called: (exit code, stderr tail).</summary>
    public event Action<int, string>? Crashed;

    public string? SessionId { get; private set; }
    /// <summary>Why rules.txt couldn't be used as-is (null if fine).</summary>
    public string? RulesProblem { get; private set; }
    public bool IsRunning => _process is { HasExited: false };

    public ClaudeSession(Dispatcher ui) => _ui = ui;

    /// <summary>Starts the process. Throws if the claude CLI can't be found or started.</summary>
    public void Start(string? model, bool allowReadAnywhere)
    {
        var launch = ClaudeCli.FindLaunch()
                     ?? throw new FileNotFoundException("Couldn't find the Claude Code CLI (claude.exe). Install Claude Code and sign in once in a terminal, then try again.");
        AppPaths.EnsureCreated();

        var psi = new ProcessStartInfo(launch.FileName)
        {
            WorkingDirectory = AppPaths.Workspace,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            StandardInputEncoding = new UTF8Encoding(false),
        };
        // A proper argument list (each value passed verbatim), never one command string.
        foreach (var a in launch.PrefixArgs) psi.ArgumentList.Add(a);
        string rules = ClaudeCli.LoadRules(out var rulesProblem);
        foreach (var a in ClaudeCli.BuildArguments(rules, model, allowReadAnywhere)) psi.ArgumentList.Add(a);
        RulesProblem = rulesProblem;

        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            lock (_stderr)
            {
                _stderr.AppendLine(e.Data);
                if (_stderr.Length > 8000) _stderr.Remove(0, _stderr.Length - 8000);
            }
        };
        p.Exited += (_, _) => _ui.BeginInvoke(() => OnExited(p));
        p.Start();
        ProcessJob.Add(p);
        p.BeginErrorReadLine();
        _process = p;

        var reader = new Thread(() => ReadLoop(p)) { IsBackground = true, Name = "claude-stdout" };
        reader.Start();
    }

    /// <summary>Sends one user message. The caller makes sure a reply isn't already in progress.</summary>
    public void Send(string text)
    {
        if (_process is not { HasExited: false } p) throw new InvalidOperationException("Claude isn't running.");
        _textInBlockSeen = false;
        var line = JsonSerializer.Serialize(new
        {
            type = "user",
            message = new { role = "user", content = text },
        });
        p.StandardInput.WriteLine(line);
        p.StandardInput.Flush();
    }

    /// <summary>Ends the session (X, new chat, quit). Kills the process and anything it started.</summary>
    public void Stop()
    {
        _stopping = true;
        var p = _process;
        _process = null;
        if (p == null) return;
        try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { /* already gone */ }
        p.Dispose();
    }

    public void Dispose() => Stop();

    // ------------------------------------------------------------------ stdout parsing

    private void ReadLoop(Process p)
    {
        try
        {
            string? line;
            while ((line = p.StandardOutput.ReadLine()) != null)
            {
                if (line.Length == 0 || line[0] != '{') continue;
                try { Handle(line); }
                catch (JsonException) { /* not an event line */ }
            }
        }
        catch { /* stream closed when the process is killed */ }
    }

    private void Handle(string line)
    {
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;
        string type = Str(root, "type");

        // Ignore anything produced inside sub-agents; only the main conversation is shown.
        if (root.TryGetProperty("parent_tool_use_id", out var parent) && parent.ValueKind == JsonValueKind.String)
            return;

        switch (type)
        {
            case "system":
                string subtype = Str(root, "subtype");
                if (subtype == "init")
                    SessionId = Str(root, "session_id");
                else if (subtype == "permission_denied")
                {
                    string msg = Str(root, "message");
                    if (msg.Length == 0) msg = $"{Str(root, "tool_name")} was blocked: {Str(root, "decision_reason")}";
                    Post(() => PermissionDenied?.Invoke(msg));
                }
                break;

            case "stream_event":
                if (!root.TryGetProperty("event", out var ev)) break;
                string evType = Str(ev, "type");
                if (evType == "content_block_start" && ev.TryGetProperty("content_block", out var block))
                {
                    string blockType = Str(block, "type");
                    _inTextBlock = blockType == "text";
                    if (_inTextBlock && _textInBlockSeen) Post(() => TextBlockStarted?.Invoke());
                }
                else if (evType == "content_block_stop" && _inTextBlock)
                {
                    _inTextBlock = false;
                    Post(() => TextBlockEnded?.Invoke());
                }
                else if (evType == "content_block_delta" && ev.TryGetProperty("delta", out var delta)
                         && Str(delta, "type") == "text_delta")
                {
                    string text = Str(delta, "text");
                    if (text.Length > 0)
                    {
                        _textInBlockSeen = true;
                        Post(() => TextDelta?.Invoke(text));
                    }
                }
                break;

            case "assistant":
                // Full message: tool calls with their complete input (for the status line).
                if (root.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content)
                    && content.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in content.EnumerateArray())
                        if (Str(item, "type") == "tool_use")
                        {
                            string name = Str(item, "name");
                            string detail = item.TryGetProperty("input", out var input) ? DescribeInput(input) : "";
                            Post(() => ToolStarted?.Invoke(name, detail));
                        }
                }
                break;

            case "result":
                bool isError = root.TryGetProperty("is_error", out var ie) && ie.ValueKind == JsonValueKind.True;
                string? error = null;
                if (isError)
                {
                    error = ClaudeCli.Redact(Str(root, "result"));
                    if (error.Length == 0) error = Str(root, "subtype");
                }
                Post(() => TurnCompleted?.Invoke(isError, error));
                break;
        }
    }

    private static string DescribeInput(JsonElement input)
    {
        foreach (var key in new[] { "file_path", "notebook_path", "pattern", "query", "url", "path" })
            if (input.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString() ?? "";
        return "";
    }

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? "" : "";

    private void Post(Action a) => _ui.BeginInvoke(a);

    private void OnExited(Process p)
    {
        if (_stopping || !ReferenceEquals(p, _process)) return; // we ended it on purpose
        int code;
        try { code = p.ExitCode; } catch { code = -1; }
        string tail;
        lock (_stderr) tail = ClaudeCli.Redact(_stderr.ToString().Trim()); // never log or show anything token-like
        _process = null;
        try
        {
            File.AppendAllText(Path.Combine(AppPaths.Logs, "claude-crash.log"),
                $"[{DateTime.Now:u}] exit {code}\n{tail}\n\n");
        }
        catch { }
        Crashed?.Invoke(code, tail);
    }
}
