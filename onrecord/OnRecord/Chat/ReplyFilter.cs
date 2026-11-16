using System.Text;

namespace OnRecord.Chat;

/// <summary>
/// A tag Claude can put in its reply. Tag lines are hidden from the chat and trigger an action.
/// To add a new tag: implement this interface and add an instance to the handler list in ChatController.
/// </summary>
public interface ITagHandler
{
    /// <summary>
    /// Called for each complete line. Return true if this line is your tag: the line is then hidden
    /// and you perform your action. <paramref name="isFirstLine"/> is true for the first non-empty
    /// line of a text block.
    /// </summary>
    bool TryHandle(string line, bool isFirstLine);

    /// <summary>
    /// Called while a line is still streaming in. Return true if it could still turn into your tag,
    /// so it is held back instead of flashing on screen.
    /// </summary>
    bool CouldBecomeTag(string partialLine, bool isFirstLine);
}

/// <summary>
/// Turns the raw streamed reply into the visible text: buffers line by line, hands complete lines to
/// the tag handlers, and holds back an incomplete line only while it might still become a tag.
/// </summary>
public sealed class ReplyFilter
{
    private readonly IReadOnlyList<ITagHandler> _handlers;
    private readonly StringBuilder _visible = new();
    private readonly StringBuilder _pending = new();  // current incomplete line
    private bool _sawContentInBlock;

    public ReplyFilter(IReadOnlyList<ITagHandler> handlers) => _handlers = handlers;

    /// <summary>The text to show right now (committed lines + the in-progress line if it's safe to show).</summary>
    public string Visible
    {
        get
        {
            string partial = _pending.ToString();
            if (partial.Length == 0 || IsPossibleTag(partial)) return TrimEnd(_visible.ToString());
            return _visible.ToString() + partial;
        }
    }

    /// <summary>A new assistant text block (e.g. after a tool call) starts; its first line may carry a tag.</summary>
    public void BeginBlock()
    {
        Flush();
        if (_visible.Length > 0 && !_visible.ToString().EndsWith("\n\n"))
            _visible.Append(_visible.ToString().EndsWith('\n') ? "\n" : "\n\n");
        _sawContentInBlock = false;
    }

    public void Append(string chunk)
    {
        foreach (char c in chunk)
        {
            if (c == '\n') CompleteLine();
            else if (c != '\r') _pending.Append(c);
        }
    }

    /// <summary>A text block ended (e.g. Claude is about to use a tool): its last line is complete.</summary>
    public void EndBlock() => Flush();

    /// <summary>The reply finished: treat the last incomplete line as complete.</summary>
    public void Flush()
    {
        if (_pending.Length > 0) CompleteLine(endOfText: true);
    }

    private bool IsFirstLine => !_sawContentInBlock;

    private bool IsPossibleTag(string partial)
    {
        foreach (var h in _handlers)
            if (h.CouldBecomeTag(partial, IsFirstLine)) return true;
        return false;
    }

    private void CompleteLine(bool endOfText = false)
    {
        string line = _pending.ToString();
        _pending.Clear();

        if (line.Trim().Length > 0)
        {
            bool first = IsFirstLine;
            foreach (var h in _handlers)
                if (h.TryHandle(line, first))
                {
                    // Tag line: hidden. Leading blank lines after a first-line tag are dropped too.
                    return;
                }
            _sawContentInBlock = true;
        }
        else if (!_sawContentInBlock)
        {
            return; // skip blank lines before the first real line of a block
        }

        _visible.Append(line);
        if (!endOfText) _visible.Append('\n');
    }

    private static string TrimEnd(string s) => s.TrimEnd('\n', ' ');
}
