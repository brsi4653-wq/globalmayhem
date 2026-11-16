using OnRecord.Claude;

namespace OnRecord.Tests;

[Collection("Modes")] // changes the shared Modes map: never in parallel with other tests that read it
public class EasterEggTests
{
    [Fact]
    public void Easter_egg_mode_is_known_but_not_listed_in_settings()
    {
        Modes.Load(new Dictionary<string, string> { ["claude"] = "#8000FF", ["code"] = "#FF8000" });
        Assert.True(Modes.IsKnown("easteregg"));
        Assert.Equal("#FFC83D", Modes.ToHex(Modes.ColorOf("easteregg")));
        Assert.DoesNotContain(Modes.Visible, kv => kv.Key == "easteregg");
        Assert.Equal(new[] { "claude", "code" }, Modes.Visible.Select(kv => kv.Key).OrderBy(k => k));
        Modes.Load(null);
    }

    [Fact]
    public void A_hand_added_easteregg_colour_is_ignored_and_dropped_from_settings()
    {
        Modes.Load(new Dictionary<string, string> { ["claude"] = "#8000FF", ["easteregg"] = "#80FF80" });
        Assert.Equal("#FFC83D", Modes.ToHex(Modes.ColorOf("easteregg")));
        Modes.Load(null);

        var s = new AppSettings { ModeColors = new() { ["claude"] = "#8000FF", ["EasterEgg"] = "#80FF80" } };
        Assert.True(s.RemoveHiddenModes());
        Assert.Equal(new[] { "claude" }, s.ModeColors.Keys);
        Assert.False(s.RemoveHiddenModes());
    }

    [Fact]
    public void Reset_to_defaults_has_no_easteregg()
    {
        Assert.DoesNotContain("easteregg", Modes.Defaults.Keys);
        Modes.Load(null);
        Assert.True(Modes.IsKnown("easteregg"));
    }

    [Fact]
    public void Pill_shows_OnRecord_for_the_easter_egg_mode()
    {
        Assert.Equal("OnRecord", Modes.DisplayName("easteregg"));
        Assert.Equal("Claude", Modes.DisplayName("claude"));
    }

    [Fact]
    public void Built_in_block_comes_after_the_user_rules_and_is_not_in_the_default_rules_txt()
    {
        string combined = BuiltInRules.AppendTo("User rules.\n[tint:claude] chat\n");
        Assert.StartsWith("User rules.\n[tint:claude] chat\n\n===EASTER EGGS START===\n", combined);
        Assert.EndsWith("===EASTER EGGS END===\n", combined);
        Assert.Contains("[tint:easteregg]", combined);
        Assert.Contains("November 16", combined);

        Assert.DoesNotContain("easteregg", RulesFile.FallbackDefault, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EASTER", RulesFile.ReadBundled(), StringComparison.OrdinalIgnoreCase);
        // The built-in block never becomes part of the tint list that Settings edits.
        Assert.DoesNotContain(RulesFile.ParseTints(combined), t => t.Name == "easteregg");
    }

    [Fact]
    public void Restore_default_rules_never_includes_the_easter_eggs()
    {
        var text = RulesFile.ReplaceTintList(RulesFile.ReadBundled(), [new RulesFile.Tint("claude", "chat")])!;
        Assert.DoesNotContain("EASTER", text, StringComparison.OrdinalIgnoreCase);
    }
}
