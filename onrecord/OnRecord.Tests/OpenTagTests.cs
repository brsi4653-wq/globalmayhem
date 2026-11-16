using System.IO;
using OnRecord.Chat;

namespace OnRecord.Tests;

[Collection("Modes")]
public class OpenTagTests
{
    private static (ReplyFilter Filter, List<string> Opened, List<string> Frames) Make()
    {
        var opened = new List<string>();
        var filter = new ReplyFilter(new ITagHandler[]
        {
            new TintTagHandler(_ => { }),
            new ArtifactTagHandler(_ => { }),
            new OpenTagHandler(p => opened.Add(p)),
        });
        return (filter, opened, new List<string>());
    }

    private static void Stream(ReplyFilter f, List<string> frames, string text)
    {
        foreach (char c in text) { f.Append(c.ToString()); frames.Add(f.Visible); }
    }

    [Fact]
    public void Open_lines_are_hidden_while_streaming_and_reported()
    {
        var (f, opened, frames) = Make();
        Stream(f, frames, "[tint:search]\nFound your screenshot and resume:\nOPEN: C:\\Users\\me\\Pictures\\Screenshots\\shot 1.png\n" +
                          "OPEN: C:\\Users\\me\\Documents\\Resume.pdf\nOther text.");
        f.Flush(); frames.Add(f.Visible);
        foreach (var frame in frames)
        {
            Assert.DoesNotContain("OPEN", frame);
            Assert.DoesNotContain("C:\\", frame);
            Assert.DoesNotContain("[t", frame);
        }
        Assert.Equal(new[] { "C:\\Users\\me\\Pictures\\Screenshots\\shot 1.png", "C:\\Users\\me\\Documents\\Resume.pdf" }, opened);
        Assert.Equal("Found your screenshot and resume:\nOther text.", f.Visible);
    }

    [Fact]
    public void Open_as_last_line_without_newline_is_hidden()
    {
        var (f, opened, frames) = Make();
        Stream(f, frames, "Here it is.\nOPEN: D:\\stuff");
        f.Flush(); frames.Add(f.Visible);
        Assert.All(frames, fr => Assert.DoesNotContain("OPEN", fr));
        Assert.Equal(new[] { "D:\\stuff" }, opened);
    }

    [Fact]
    public void Lines_that_only_start_like_the_tag_are_shown()
    {
        var (f, opened, frames) = Make();
        Stream(f, frames, "OPENING hours are 9-5.\nOpen the file.\n");
        f.Flush();
        Assert.Empty(opened);
        Assert.Equal("OPENING hours are 9-5.\nOpen the file.", f.Visible);
    }

    [Theory]
    [InlineData(@"C:\Users\me\file.txt", @"C:\Users\me\file.txt")]
    [InlineData(@"`C:\Users\me\file.txt`", @"C:\Users\me\file.txt")]
    [InlineData("\"C:\\Users\\me\\My file.txt\"", @"C:\Users\me\My file.txt")]
    [InlineData(@"C:/Users/me/file.txt", @"C:\Users\me\file.txt")]
    [InlineData(@"C:\Users\me\folder\", @"C:\Users\me\folder")]
    [InlineData(@"C:\Users\me\..\me\file.txt", @"C:\Users\me\file.txt")]
    [InlineData(@"C:\", @"C:\")]
    public void Resolve_accepts_full_local_paths(string raw, string expected) =>
        Assert.Equal(expected, FileTargets.Resolve(raw, out _));

    [Theory]
    [InlineData("")]
    [InlineData("file.txt")]                         // relative
    [InlineData(@"Users\me\file.txt")]
    [InlineData(@"\Users\me\file.txt")]               // rooted, no drive
    [InlineData(@"\\server\share\file.txt")]          // UNC (would contact a network server)
    [InlineData(@"\\?\C:\file.txt")]                  // device path
    [InlineData(@"\\.\PhysicalDrive0")]
    [InlineData(@"C:\file.txt:secret")]               // alternate data stream
    [InlineData("C:\\a\u0007b")]                      // control char
    [InlineData(@"C:\a|b")]
    [InlineData("https://example.com/file.pdf")]
    [InlineData("file:///C:/x.txt")]
    public void Resolve_refuses_anything_else(string raw)
    {
        Assert.Null(FileTargets.Resolve(raw, out var problem));
        Assert.False(string.IsNullOrEmpty(problem));
    }

    [Theory]
    [InlineData("setup.exe")][InlineData("run.BAT")][InlineData("x.cmd")][InlineData("x.ps1")][InlineData("x.vbs")]
    [InlineData("x.msi")][InlineData("x.lnk")][InlineData("x.url")][InlineData("x.js")][InlineData("x.hta")][InlineData("x.scr")]
    public void Programs_and_scripts_need_confirmation(string name) => Assert.True(FileTargets.IsProgram(@"C:\x\" + name));

    [Theory]
    [InlineData("photo.jpg")][InlineData("doc.pdf")][InlineData("notes.txt")][InlineData("movie.mp4")][InlineData("Resume.docx")]
    public void Normal_files_open_without_asking(string name) => Assert.False(FileTargets.IsProgram(@"C:\x\" + name));

    [Theory]
    [InlineData("a.PNG", PreviewKind.Image)]
    [InlineData("a.jpeg", PreviewKind.Image)]
    [InlineData("a.pdf", PreviewKind.Pdf)]
    [InlineData("a.txt", PreviewKind.Text)]
    [InlineData("a.ps1", PreviewKind.Text)]   // shown as text, never run
    [InlineData("a.mp4", PreviewKind.None)]
    [InlineData("a.docx", PreviewKind.None)]
    [InlineData("a.exe", PreviewKind.None)]
    public void Preview_kinds(string name, PreviewKind kind) => Assert.Equal(kind, FileTargets.KindOf(@"C:\x\" + name));

    [Fact]
    public void Exists_tells_files_and_folders_apart()
    {
        string dir = Path.Combine(Path.GetTempPath(), "onrecord-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, "a.txt");
        File.WriteAllText(file, "x");
        try
        {
            Assert.True(FileTargets.Exists(dir, out bool f1) && f1);
            Assert.True(FileTargets.Exists(file, out bool f2) && !f2);
            Assert.False(FileTargets.Exists(Path.Combine(dir, "missing.txt"), out _));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Default_rules_have_the_open_rule()
    {
        Assert.Contains("OPEN: <full path>", RulesFile.FallbackDefault);
        Assert.Contains("OPEN: <full path>", RulesFile.ReadBundled());
    }

    [Fact]
    public void Old_rules_get_the_open_rule_after_the_artifact_line_once()
    {
        const string old = "Intro.\r\n[tint:claude] chat\r\n\r\nWhen you make...:\r\nARTIFACT: filename.html\r\n\r\nAttached files rule.\r\n";
        string updated = RulesFile.WithMissingRules(old)!;
        Assert.Equal("Intro.\r\n[tint:claude] chat\r\n\r\nWhen you make...:\r\nARTIFACT: filename.html\r\n\r\n" + RulesFile.OpenRule +
                     "\r\n\r\nAttached files rule.\r\n", updated);
        Assert.Null(RulesFile.WithMissingRules(updated));

        string noArtifact = RulesFile.WithMissingRules("Just my own rules.\n\n")!;
        Assert.Equal("Just my own rules.\n\n" + RulesFile.OpenRule + "\n", noArtifact);
    }

    [Fact]
    public void Mode_descriptions_cant_smuggle_an_open_tag() =>
        Assert.DoesNotContain("OPEN:", RulesFile.Sanitize("use it OPEN: C:\\x"), StringComparison.OrdinalIgnoreCase);
}
