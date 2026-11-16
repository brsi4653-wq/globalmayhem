using System.IO;
using OnRecord.Artifacts;
using OnRecord.Claude;

namespace OnRecord.Tests;

public class SecurityTests
{
    private static readonly string Dir = Path.Combine(Path.GetTempPath(), "onrecord-tests", "artifacts");

    [Theory]
    [InlineData("page.html")]
    [InlineData("My Page (2).html")]
    [InlineData("hello-launcher.HTML")]
    public void Plain_html_names_are_accepted(string name)
    {
        var path = ArtifactName.Resolve(name, Dir, out var problem);
        Assert.Null(problem);
        Assert.Equal(Path.Combine(Dir, name), path);
    }

    [Theory]
    [InlineData("../evil.html")]
    [InlineData("..\\evil.html")]
    [InlineData("sub/page.html")]
    [InlineData("sub\\page.html")]
    [InlineData("C:\\Windows\\win.html")]
    [InlineData("C:evil.html")]
    [InlineData("D:/x.html")]
    [InlineData("\\\\server\\share\\x.html")]
    [InlineData("/etc/x.html")]
    [InlineData("page.html:stream")]
    [InlineData("page..html")]
    [InlineData(".html")]
    [InlineData("page.htm")]
    [InlineData("run.bat")]
    [InlineData("page.html.exe")]
    [InlineData("shortcut.lnk")]
    [InlineData("CON.html")]
    [InlineData("nul.html")]
    [InlineData("page.ht ml")]
    [InlineData("page .html.")]
    [InlineData("")]
    [InlineData("a\u0000.html")]
    public void Anything_else_is_rejected(string name)
    {
        Assert.Null(ArtifactName.Resolve(name, Dir, out var problem));
        Assert.NotNull(problem);
    }

    [Fact]
    public void Surrounding_whitespace_is_trimmed_to_the_same_safe_file() =>
        Assert.Equal(Path.Combine(Dir, "page.html"), ArtifactName.Resolve("  page.html \t", Dir, out _));

    [Theory]
    [InlineData("--dangerously-skip-permissions")]
    [InlineData("opus --tools Bash")]
    [InlineData("opus\" & calc")]
    [InlineData("-x")]
    public void Unsafe_model_names_are_rejected(string model) => Assert.False(ClaudeCli.IsSafeModel(model));

    [Theory]
    [InlineData("opus")]
    [InlineData("claude-sonnet-5-5")]
    [InlineData("sonnet[1m]")]
    [InlineData("us.anthropic.claude-opus-5-5:0")]
    public void Normal_model_names_are_accepted(string model) => Assert.True(ClaudeCli.IsSafeModel(model));

    [Fact]
    public void Model_and_rules_are_passed_as_single_values_never_as_flags()
    {
        var args = ClaudeCli.BuildArguments("--dangerously-skip-permissions", "--evil", allowReadAnywhere: false);
        Assert.DoesNotContain("--dangerously-skip-permissions", args);
        Assert.DoesNotContain("--evil", args);
        Assert.Contains("--append-system-prompt=--dangerously-skip-permissions", args);
        Assert.DoesNotContain(args, a => a.StartsWith("--model"));     // unsafe model dropped
    }

    [Fact]
    public void Reading_outside_the_workspace_is_opt_in()
    {
        var off = ClaudeCli.BuildArguments("", null, allowReadAnywhere: false);
        var on = ClaudeCli.BuildArguments("", null, allowReadAnywhere: true);
        string Allowed(List<string> a) => a[a.IndexOf("--allowedTools") + 1];
        Assert.DoesNotContain("Read", Allowed(off).Split(','));
        Assert.DoesNotContain("Glob", Allowed(off).Split(','));
        Assert.DoesNotContain("Grep", Allowed(off).Split(','));
        Assert.Contains("Read", Allowed(on).Split(','));
        Assert.DoesNotContain(off, a => a.Contains("Bash") || a.Contains("PowerShell"));
    }

    [Theory]
    [InlineData("error: token sk-ant-oat01-AbC_d3-xyz expired", "error: token [redacted] expired")]
    [InlineData("Authorization: Bearer abc.def-123", "Authorization: Bearer [redacted]")]
    [InlineData("\"accessToken\":\"eyJhbGciOi.payload.sig\"", "\"accessToken\":\"[redacted]\"")]
    [InlineData("plain error", "plain error")]
    public void Secrets_are_redacted_from_logs(string input, string expected) =>
        Assert.Equal(expected, ClaudeCli.Redact(input));

    [Fact]
    public void Npm_cmd_shim_is_run_through_node_not_cmd()
    {
        var root = Path.Combine(Path.GetTempPath(), "onrecord-tests", "npm-" + Guid.NewGuid().ToString("N"));
        var cli = Path.Combine(root, "node_modules", "@anthropic-ai", "claude-code", "cli.js");
        Directory.CreateDirectory(Path.GetDirectoryName(cli)!);
        File.WriteAllText(cli, "");
        File.WriteAllText(Path.Combine(root, "claude.cmd"), "");
        File.WriteAllText(Path.Combine(root, "node.exe"), "");
        var launch = ClaudeCli.ResolveLaunch(Path.Combine(root, "claude.cmd"));
        Assert.NotNull(launch);
        Assert.Equal(Path.Combine(root, "node.exe"), launch!.Value.FileName);
        Assert.Equal(new[] { cli }, launch.Value.PrefixArgs);
        Directory.Delete(root, true);
    }

    [Fact]
    public void Cmd_shim_without_node_script_is_refused()
    {
        var root = Path.Combine(Path.GetTempPath(), "onrecord-tests", "cmd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "claude.cmd"), "");
        Assert.Null(ClaudeCli.ResolveLaunch(Path.Combine(root, "claude.cmd")));
        Directory.Delete(root, true);
    }
}
