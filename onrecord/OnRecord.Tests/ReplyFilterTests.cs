using OnRecord.Chat;

namespace OnRecord.Tests;

public class ReplyFilterTests
{
    private sealed class Harness
    {
        public readonly List<string> Modes = new();
        public readonly List<string> Artifacts = new();
        public readonly ReplyFilter Filter;
        public readonly List<string> Frames = new(); // every visible state seen during streaming

        public Harness()
        {
            Filter = new ReplyFilter(new ITagHandler[]
            {
                new TintTagHandler(m => Modes.Add(m)),
                new ArtifactTagHandler(a => Artifacts.Add(a)),
            });
        }

        /// <summary>Streams text one character at a time, recording what the user would see after each.</summary>
        public void Stream(string text)
        {
            foreach (char c in text)
            {
                Filter.Append(c.ToString());
                Frames.Add(Filter.Visible);
            }
        }

        public void EndBlock() { Filter.EndBlock(); Frames.Add(Filter.Visible); }
        public void BeginBlock() { Filter.BeginBlock(); Frames.Add(Filter.Visible); }
        public void End() { Filter.Flush(); Frames.Add(Filter.Visible); }

        public void AssertNoTagEverVisible()
        {
            foreach (var f in Frames)
            {
                Assert.DoesNotContain("[tint", f, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("ARTIFACT", f);
                Assert.DoesNotContain("[t", f);
            }
        }
    }

    [Fact]
    public void Tint_tag_then_text_never_visible()
    {
        var h = new Harness();
        h.Stream("[tint:code]\nHere is the code.\n");
        h.End();
        h.AssertNoTagEverVisible();
        Assert.Equal(new[] { "code" }, h.Modes);
        Assert.Equal("Here is the code.", h.Filter.Visible.Trim());
    }

    [Fact]
    public void Tint_tag_alone_in_block_before_tool_call_is_applied_at_block_end_and_never_shown()
    {
        // Coding request shape: tag, then the text block ends and Claude runs tools for a while.
        var h = new Harness();
        h.Stream("[tint:code]");
        Assert.Equal("", h.Filter.Visible);     // complete tag, no newline yet: still hidden
        Assert.Empty(h.Modes);
        h.EndBlock();                           // block ends -> tag applied
        Assert.Equal(new[] { "code" }, h.Modes);
        h.BeginBlock();
        h.Stream("I wrote the file.");
        h.End();
        h.AssertNoTagEverVisible();
        Assert.Equal("I wrote the file.", h.Filter.Visible.Trim());
    }

    [Fact]
    public void Artifact_line_never_visible_and_reported()
    {
        var h = new Harness();
        h.Stream("[tint:code]\nDone, here it is.\nARTIFACT: page.html\nEnjoy.");
        h.End();
        h.AssertNoTagEverVisible();
        Assert.Equal(new[] { "page.html" }, h.Artifacts);
        Assert.Contains("Enjoy.", h.Filter.Visible);
    }

    [Fact]
    public void Artifact_as_last_line_without_newline()
    {
        var h = new Harness();
        h.Stream("Made it.\nARTIFACT: a.html");
        h.End();
        h.AssertNoTagEverVisible();
        Assert.Equal(new[] { "a.html" }, h.Artifacts);
    }

    [Fact]
    public void Unknown_tint_is_hidden_and_mode_unchanged()
    {
        var h = new Harness();
        h.Stream("[tint:nonsense]\nHello");
        h.End();
        h.AssertNoTagEverVisible();
        Assert.Empty(h.Modes);
        Assert.Equal("Hello", h.Filter.Visible);
    }

    [Fact]
    public void Normal_text_streams_immediately()
    {
        var h = new Harness();
        h.Stream("Hello th");
        Assert.Equal("Hello th", h.Filter.Visible); // not held back: clearly not a tag
    }

    [Fact]
    public void Bracket_text_that_is_not_a_tag_is_shown_after_line_ends()
    {
        var h = new Harness();
        h.Stream("[tint is a word]\nok");
        h.End();
        Assert.Contains("[tint is a word]", h.Filter.Visible);
    }

    [Fact]
    public void Tag_after_leading_blank_lines()
    {
        var h = new Harness();
        h.Stream("\n\n[tint:web]\nResult");
        h.End();
        h.AssertNoTagEverVisible();
        Assert.Equal(new[] { "web" }, h.Modes);
    }
}
