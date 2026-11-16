using System.IO;
using OnRecord.Chat;

namespace OnRecord.Tests;

public class AttachmentTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "onrecord-tests", "att-" + Guid.NewGuid().ToString("N"));
    private string Src => Path.Combine(_root, "src");
    private string Dest => Path.Combine(_root, "attachments");

    public AttachmentTests() => Directory.CreateDirectory(Src);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private string MakeFile(string name, string content)
    {
        var p = Path.Combine(Src, name);
        File.WriteAllText(p, content);
        return p;
    }

    [Fact]
    public void Copies_never_overwrite_and_originals_are_untouched()
    {
        var original = MakeFile("notes.txt", "original");
        var stamp = File.GetLastWriteTimeUtc(original);
        Directory.CreateDirectory(Dest);
        File.WriteAllText(Path.Combine(Dest, "notes.txt"), "already here");

        var a = Attachments.FromFile(original, out _)!;
        var saved = Attachments.SaveAll(new[] { a, a }, Dest);

        Assert.Equal(new[] { Path.Combine(Dest, "notes (2).txt"), Path.Combine(Dest, "notes (3).txt") }, saved);
        Assert.Equal("already here", File.ReadAllText(Path.Combine(Dest, "notes.txt")));
        Assert.Equal("original", File.ReadAllText(saved[0]));
        Assert.Equal("original", File.ReadAllText(original));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(original));
    }

    [Fact]
    public void Files_over_30_MB_are_refused_with_a_clear_message()
    {
        var big = Path.Combine(Src, "big.bin");
        using (var fs = File.Create(big)) fs.SetLength(Attachments.MaxBytes + 1);
        Assert.Null(Attachments.FromFile(big, out var problem));
        Assert.Contains("30 MB", problem);
        var ok = Path.Combine(Src, "ok.bin");
        using (var fs = File.Create(ok)) fs.SetLength(Attachments.MaxBytes);
        Assert.NotNull(Attachments.FromFile(ok, out _));
    }

    [Fact]
    public void Folders_and_missing_files_are_refused()
    {
        Assert.Null(Attachments.FromFile(Src, out var p1)); Assert.Contains("folder", p1);
        Assert.Null(Attachments.FromFile(Path.Combine(Src, "nope.pdf"), out var p2)); Assert.NotNull(p2);
    }

    [Fact]
    public void Message_lists_full_paths_after_the_text()
    {
        Assert.Equal("Look at this\n\nAttached files:\nC:\\a\\x.png\nC:\\a\\y.pdf",
            Attachments.BuildMessage("Look at this", new[] { "C:\\a\\x.png", "C:\\a\\y.pdf" }));
        Assert.Equal("Attached files:\nC:\\a\\x.png", Attachments.BuildMessage("", new[] { "C:\\a\\x.png" }));
        Assert.Equal("just text", Attachments.BuildMessage("just text", Array.Empty<string>()));
    }

    [Fact]
    public void Pasted_bytes_are_saved_as_png()
    {
        var a = new PendingAttachment { DisplayName = "screenshot-1.png", PngBytes = new byte[] { 1, 2, 3 }, Size = 3 };
        var saved = Attachments.SaveAll(new[] { a }, Dest);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(saved[0]));
    }

    [Theory]
    [InlineData("a:b.txt", "a_b.txt")]
    [InlineData("...", "attachment")]
    [InlineData("report.pdf", "report.pdf")]
    public void File_names_are_made_safe(string input, string expected) => Assert.Equal(expected, Attachments.SafeFileName(input));
}
