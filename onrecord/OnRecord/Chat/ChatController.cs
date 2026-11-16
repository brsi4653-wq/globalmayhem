using System.IO;
using System.Windows.Threading;
using OnRecord.Claude;

namespace OnRecord.Chat;

/// <summary>
/// Connects the chat UI to one persistent Claude session:
///  - StartSession() when the popup opens (fresh, no context), EndSession() when it is closed with X
///  - messages sent while Claude is replying are queued silently and sent when the reply finishes
///  - if the claude process dies unexpectedly, say so in the chat; the next message starts a new process
/// </summary>
public sealed class ChatController
{
    private readonly ChatPanel _panel;
    private readonly Dispatcher _ui;
    private readonly Func<string?> _model;
    private readonly Func<bool> _allowReadAnywhere;
    private readonly HashSet<string> _shownProblems = new(); // show each rules.txt problem once per app run
    private readonly List<ITagHandler> _tagHandlers;
    private readonly Queue<string> _queue = new();

    private ClaudeSession? _session;
    private bool _busy;
    private AssistantMessageView? _reply;
    private ReplyFilter? _filter;

    /// <param name="onMode">Called when Claude's reply carries a known [tint:mode] tag.</param>
    /// <param name="onArtifact">Called with the file name from an "ARTIFACT: name" line.</param>
    public ChatController(ChatPanel panel, Dispatcher ui, Func<string?> model, Func<bool> allowReadAnywhere, Action<string> onMode, Action<string> onArtifact)
    {
        _panel = panel;
        _ui = ui;
        _model = model;
        _allowReadAnywhere = allowReadAnywhere;
        // The reply tags OnRecord understands. Add new ITagHandler implementations here.
        _tagHandlers =
        [
            new TintTagHandler(onMode),
            new ArtifactTagHandler(name =>
            {
                _reply?.AddArtifactChip(name, () => onArtifact(name));
                onArtifact(name);
            }),
        ];
        _panel.Submitted += Submit;
    }

    public bool IsBusy => _busy;

    /// <summary>Starts a brand-new Claude process (no previous context).</summary>
    public void StartSession()
    {
        EndSession();
        var session = new ClaudeSession(_ui);
        session.TextBlockStarted += () => { if (IsCurrent(session)) _filter?.BeginBlock(); };
        session.TextBlockEnded += () =>
        {
            if (!IsCurrent(session) || _filter == null || _reply == null) return;
            _filter.EndBlock();                    // a held-back tag line is complete now: apply it, never show it
            _reply.SetMarkdown(_filter.Visible);
        };
        session.TextDelta += t => { if (IsCurrent(session)) OnText(t); };
        session.ToolStarted += (name, detail) => { if (IsCurrent(session)) _reply?.SetStatus(DescribeTool(name, detail)); };
        session.PermissionDenied += msg => { if (IsCurrent(session)) OnBlocked(msg); };
        session.TurnCompleted += (isError, error) => { if (IsCurrent(session)) OnTurnCompleted(isError, error); };
        session.Crashed += (code, stderr) => { if (IsCurrent(session)) OnCrashed(code, stderr); };
        try
        {
            session.Start(_model(), _allowReadAnywhere());
            _session = session;
            if (session.RulesProblem is { } problem && _shownProblems.Add(problem))
                _panel.AddNotice(NoticeKind.Info, problem);
        }
        catch (Exception ex)
        {
            _panel.AddNotice(NoticeKind.Error, $"Couldn't start Claude: {ex.Message}");
            _session = null;
        }
    }

    /// <summary>Ends the current session and forgets everything about it.</summary>
    public void EndSession()
    {
        var s = _session;
        _session = null;
        s?.Stop();
        _busy = false;
        _queue.Clear();
        _reply = null;
        _filter = null;
    }

    /// <summary>New chat button: clear the chat and start a fresh session.</summary>
    public void NewChat()
    {
        EndSession();
        _panel.Clear();
        StartSession();
    }

    public void Submit(string text, IReadOnlyList<PendingAttachment> attachments)
    {
        List<string> paths;
        try
        {
            // Copies go into the workspace (Claude may read there without extra permission); originals are untouched.
            paths = Attachments.SaveAll(attachments, AppPaths.Attachments);
        }
        catch (Exception ex)
        {
            _panel.AddUserMessage(text, attachments.Select(a => a.DisplayName).ToList());
            _panel.AddNotice(NoticeKind.Error, $"Couldn't copy the attached files, so the message wasn't sent: {ex.Message}");
            return;
        }
        _panel.AddUserMessage(text, paths.Select(Path.GetFileName).ToList()!);
        text = Attachments.BuildMessage(text, paths);
        if (_session is not { IsRunning: true })
        {
            StartSession(); // first message after a crash (or the start failed earlier)
            if (_session == null) return;
        }
        if (_busy)
        {
            _queue.Enqueue(text); // silent queue: sent when the current reply finishes
            return;
        }
        SendNow(text);
    }

    private void SendNow(string text)
    {
        _busy = true;
        _filter = new ReplyFilter(_tagHandlers);
        _reply = _panel.BeginAssistantMessage();
        _reply.SetStatus("Thinking…");
        try
        {
            _session!.Send(text);
        }
        catch (Exception ex)
        {
            _busy = false;
            _reply.SetStatus(null);
            _reply.AddNotice(NoticeKind.Error, $"Couldn't send the message: {ex.Message}");
        }
    }

    private bool IsCurrent(ClaudeSession s) => ReferenceEquals(s, _session);

    private void OnText(string text)
    {
        if (_filter == null || _reply == null) return;
        _filter.Append(text);
        _reply.SetMarkdown(_filter.Visible);
        _reply.SetStatus(null);
        Trace(_filter.Visible);
    }

    // Diagnostics only: set ONRECORD_TRACE_VISIBLE=1 to log every visible reply state (used to prove tags never show).
    private static readonly bool TraceEnabled = Environment.GetEnvironmentVariable("ONRECORD_TRACE_VISIBLE") == "1";
    private static void Trace(string visible)
    {
        if (!TraceEnabled) return;
        try { File.AppendAllText(Path.Combine(AppPaths.Logs, "visible-trace.log"), visible.Replace("\n", "\\n") + "\n"); } catch { }
    }

    private void OnBlocked(string message)
    {
        if (_reply != null) _reply.AddNotice(NoticeKind.Blocked, message);
        else _panel.AddNotice(NoticeKind.Blocked, message);
    }

    private void OnTurnCompleted(bool isError, string? error)
    {
        if (_reply != null && _filter != null)
        {
            _filter.Flush();
            _reply.SetMarkdown(_filter.Visible, immediate: true);
            _reply.SetStatus(null);
            if (isError) _reply.AddNotice(NoticeKind.Error, FriendlyError(error));
            if (_reply.IsEmpty) _panel.Remove(_reply);
        }
        _reply = null;
        _filter = null;
        _busy = false;

        if (_queue.Count > 0) SendNow(_queue.Dequeue());
    }

    private void OnCrashed(int exitCode, string stderr)
    {
        _session = null;
        _busy = false;
        string detail = LastLines(stderr, 4);
        string text = $"Claude stopped unexpectedly (exit code {exitCode}). Your next message will start a fresh session without the earlier context."
                      + (detail.Length > 0 ? $"\n{detail}" : "")
                      + (_queue.Count > 0 ? $"\n{_queue.Count} message(s) you sent while it was replying weren't delivered." : "");
        _queue.Clear();
        if (_reply != null)
        {
            _filter?.Flush();
            if (_filter != null) _reply.SetMarkdown(_filter.Visible, immediate: true);
            _reply.SetStatus(null);
            _reply.AddNotice(NoticeKind.Error, text);
        }
        else
        {
            _panel.AddNotice(NoticeKind.Error, text);
        }
        _reply = null;
        _filter = null;
    }

    /// <summary>Turns the CLI's sign-in errors into steps the user can follow.</summary>
    internal static string FriendlyError(string? error)
    {
        if (string.IsNullOrWhiteSpace(error)) return "Claude reported an error.";
        string e = error.ToLowerInvariant();
        if (e.Contains("/login") || e.Contains("not logged in") || e.Contains("invalid api key") || e.Contains("please log in")
            || e.Contains("oauth token has expired") || e.Contains("authentication"))
            return "Claude Code isn't signed in. Open a terminal (PowerShell), run claude, sign in with your Claude Pro or Max account, " +
                   "then start a new chat here.\n(" + error.Trim() + ")";
        return error;
    }

    private static string DescribeTool(string name, string detail)
    {
        string file = detail.Length > 0 ? Path.GetFileName(detail.TrimEnd('/', '\\')) : "";
        return name switch
        {
            "Read" => file.Length > 0 ? $"Reading {file}…" : "Reading a file…",
            "Glob" or "Grep" => "Searching files…",
            "Write" or "Edit" or "NotebookEdit" => file.Length > 0 ? $"Writing {file}…" : "Writing a file…",
            "WebSearch" => detail.Length > 0 ? $"Searching the web: {detail}" : "Searching the web…",
            "WebFetch" => Uri.TryCreate(detail, UriKind.Absolute, out var u) ? $"Opening {u.Host}…" : "Opening a web page…",
            "ToolSearch" => "Thinking…",
            _ => $"Using {name}…",
        };
    }

    private static string LastLines(string s, int n)
    {
        var lines = s.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join("\n", lines.Skip(Math.Max(0, lines.Length - n)));
    }
}
