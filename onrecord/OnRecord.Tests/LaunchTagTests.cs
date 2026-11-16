using System.IO;
using OnRecord.Chat;
using OnRecord.Platform;

namespace OnRecord.Tests;

[Collection("Modes")]
public class LaunchTagTests
{
    private static readonly AppEntry[] Apps =
    [
        new("Spotify", "SpotifyAB.SpotifyMusic_x!Spotify"),
        new("Discord", "com.squirrel.Discord.Discord"),
        new("Notepad", "Microsoft.WindowsNotepad_x!App"),
        new("Notepad++", "{6D809377}\\Notepad++\\notepad++.exe"),
        new("Calculator", "Microsoft.WindowsCalculator_x!App"),
        new("Visual Studio Code", "Microsoft.VisualStudioCode"),
        new("Visual Studio 2022", "VisualStudio.17"),
        new("Steam", "{7C5A40EF}\\Steam\\Steam.exe"),
        new("SteamVR", "steam://rungameid/250820"),
        new("GeoGuessr Steam Edition", "steam://rungameid/3478870"),
    ];

    [Fact]
    public void Launch_line_is_hidden_while_streaming_and_only_reported_when_complete()
    {
        var launched = new List<string>();
        var f = new ReplyFilter(new ITagHandler[]
        {
            new TintTagHandler(_ => { }), new ArtifactTagHandler(_ => { }), new OpenTagHandler(_ => { }),
            new WebTagHandler(_ => { }), new LaunchTagHandler(n => launched.Add(n)),
        });
        var frames = new List<string>();
        foreach (char c in "[tint:claude]\nOpening Spotify for you.\nLAUNCH: Spotify\nEnjoy!")
        {
            f.Append(c.ToString());
            frames.Add(f.Visible);
            Assert.All(launched, n => Assert.Equal("Spotify", n)); // never a half-written name
        }
        f.Flush(); frames.Add(f.Visible);
        Assert.All(frames, fr => Assert.DoesNotContain("LAUNCH", fr));
        Assert.Equal(new[] { "Spotify" }, launched);
        Assert.Equal("Opening Spotify for you.\nEnjoy!", f.Visible);
    }

    [Theory]
    [InlineData("Spotify", "Spotify")]
    [InlineData("spotify", "Spotify")]
    [InlineData("DISCORD", "Discord")]
    [InlineData("Notepad", "Notepad")]          // exact wins over Notepad++
    [InlineData("Steam", "Steam")]              // exact wins over SteamVR and the Steam game
    [InlineData("GeoGuessr", "GeoGuessr Steam Edition")]
    [InlineData("the Spotify app", "Spotify")]
    [InlineData("Spotfy", "Spotify")]           // one letter off
    [InlineData("calculater", "Calculator")]
    public void One_clear_match_opens(string query, string expected)
    {
        var r = AppMatcher.Match(query, Apps);
        Assert.Equal(AppMatcher.Kind.One, r.Kind);
        Assert.Equal(expected, r.Apps[0].Name);
    }

    [Fact]
    public void Several_matches_ask_instead_of_guessing()
    {
        var r = AppMatcher.Match("Visual Studio", Apps);
        Assert.Equal(AppMatcher.Kind.Several, r.Kind);
        Assert.Equal(new[] { "Visual Studio 2022", "Visual Studio Code" }, r.Apps.Select(a => a.Name).Order());
    }

    [Theory]
    [InlineData("Flibbertigibbet Pro")]
    [InlineData("")]
    [InlineData("x")]
    public void No_match_opens_nothing(string query) => Assert.Equal(AppMatcher.Kind.None, AppMatcher.Match(query, Apps).Kind);

    [Theory]
    [InlineData(@"C:\Windows\System32\cmd.exe")]
    [InlineData("cmd.exe")]
    [InlineData("steam://rungameid/250820")]
    [InlineData("%COMSPEC%")]
    [InlineData("../evil")]
    public void Paths_urls_and_commands_are_refused(string name) => Assert.True(AppMatcher.LooksLikePath(name));

    [Theory]
    [InlineData("open spotify", true)]
    [InlineData("Can you launch Discord?", true)]
    [InlineData("start notepad please", true)]
    [InlineData("fire up steam", true)]
    [InlineData("what is spotify?", false)]
    [InlineData("tell me a joke about Discord", false)]
    [InlineData("", false)]
    public void Launch_only_when_the_user_asked_to_open_something(string message, bool expected) =>
        Assert.Equal(expected, LaunchIntent.Asked(message));

    [Fact]
    public void Default_rules_have_the_launch_rule_once()
    {
        Assert.Contains(RulesFile.LaunchRule, RulesFile.FallbackDefault);
        Assert.Equal(1, RulesFile.FallbackDefault.Split("LAUNCH:").Length - 1);
        Assert.DoesNotContain("LAUNCH", RulesFile.Sanitize("LAUNCH: Spotify"));
        var shipped = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "rules.txt")).Replace("\r\n", "\n");
        Assert.Contains(RulesFile.LaunchRule, shipped);
    }
}
