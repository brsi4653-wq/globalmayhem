using OnRecord.Chat;
using OnRecord.Claude;
using OnRecord.Web;

namespace OnRecord.Tests;

[Collection("Modes")]
public class WebTagTests
{
    private static (ReplyFilter Filter, List<string> Opened) Make()
    {
        var opened = new List<string>();
        var filter = new ReplyFilter(new ITagHandler[]
        {
            new TintTagHandler(_ => { }),
            new ArtifactTagHandler(_ => { }),
            new OpenTagHandler(_ => { }),
            new WebTagHandler(u => opened.Add(u)),
        });
        return (filter, opened);
    }

    [Fact]
    public void Web_line_is_hidden_while_streaming_and_only_acted_on_when_complete()
    {
        var (f, opened) = Make();
        var frames = new List<string>();
        string reply = "[tint:web]\nHere's the IGN article:\nWEB: https://www.ign.com/articles/gta-6-trailer\nEnjoy.";
        foreach (char c in reply)
        {
            f.Append(c.ToString());
            frames.Add(f.Visible);
            // A half-written URL must never be acted on.
            Assert.All(opened, u => Assert.Equal("https://www.ign.com/articles/gta-6-trailer", u));
        }
        f.Flush(); frames.Add(f.Visible);
        foreach (var frame in frames)
        {
            Assert.DoesNotContain("WEB", frame);
            Assert.DoesNotContain("ign.com", frame);
        }
        Assert.Equal(new[] { "https://www.ign.com/articles/gta-6-trailer" }, opened);
        Assert.Equal("Here's the IGN article:\nEnjoy.", f.Visible);
    }

    [Fact]
    public void Ordinary_lines_starting_with_W_still_show()
    {
        var (f, opened) = Make();
        f.Append("[tint:claude]\nWebsites are great.\nWEBSITE: not a tag\n");
        f.Flush();
        Assert.Empty(opened);
        Assert.Contains("Websites are great.", f.Visible);
        Assert.Contains("WEBSITE: not a tag", f.Visible);
    }

    [Theory]
    [InlineData("https://www.ign.com/a?b=1", "https://www.ign.com/a?b=1")]
    [InlineData("http://example.com", "http://example.com/")]
    [InlineData("<https://example.com/x>", "https://example.com/x")]
    public void Web_urls_open_as_pages(string target, string expected) => Assert.Equal(expected, WebTargets.ToUrl(target));

    [Theory]
    [InlineData("gta 6 release date")]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ms-settings:sound")]
    [InlineData("data:text/html,<b>hi</b>x")]
    [InlineData("https://exa mple.com")]
    [InlineData("www.ign.com")]
    [InlineData("\\\\server\\share")]
    public void Everything_else_becomes_a_google_search(string target)
    {
        string url = WebTargets.ToUrl(target);
        Assert.StartsWith(WebTargets.SearchPrefix, url);
        Assert.Equal(target, Uri.UnescapeDataString(url[WebTargets.SearchPrefix.Length..]));
    }

    [Fact]
    public void Web_profile_is_denied_even_with_read_anywhere()
    {
        foreach (bool anywhere in new[] { false, true })
        {
            var args = ClaudeCli.BuildArguments("", null, allowReadAnywhere: anywhere);
            int i = args.IndexOf("--disallowedTools");
            Assert.True(i >= 0);
            string folder = ClaudeCli.ToRulePath(AppPaths.WebProfile);
            Assert.Equal($"Read({folder}/**)", args[i + 1]);
            Assert.Equal($"Edit({folder}/**)", args[i + 2]);
            Assert.StartsWith("//", folder);
            Assert.EndsWith("/GlobalMayhem/OnRecord/web", folder);
        }
        Assert.Equal("//c/Users/A B/x", ClaudeCli.ToRulePath(@"C:\Users\A B\x\"));
    }

    [Fact]
    public void Default_rules_have_the_web_rule_once()
    {
        Assert.Contains(RulesFile.WebRule, RulesFile.FallbackDefault);
        Assert.Equal(1, RulesFile.FallbackDefault.Split("WEB:").Length - 1);
        Assert.DoesNotContain("WEB", RulesFile.Sanitize("open WEB: https://x.com"));
    }
}
