using System.IO;
using System.Text;
using System.Windows.Media.Imaging;

namespace OnRecord.Chat;

/// <summary>
/// A file waiting in the input bar. Either an existing file on disk (picker, drag and drop, pasted file) or
/// image bytes pasted from the clipboard (a screenshot), which only become a file when the message is sent.
/// </summary>
public sealed class PendingAttachment
{
    public required string DisplayName { get; init; }
    public string? SourcePath { get; init; }
    public byte[]? PngBytes { get; init; }
    public long Size { get; init; }
    public BitmapSource? Thumbnail { get; init; }
    public bool IsImage => Thumbnail != null;
}

public static class Attachments
{
    public const long MaxBytes = 30L * 1024 * 1024;

    private static readonly HashSet<string> ImageExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };

    public static string FormatSize(long bytes) =>
        bytes >= 1024 * 1024 ? $"{bytes / (1024.0 * 1024):0.#} MB" : $"{Math.Max(1, bytes / 1024)} KB";

    /// <summary>A file chosen by the user. Returns null with a reason if it can't be attached.</summary>
    public static PendingAttachment? FromFile(string path, out string? problem)
    {
        problem = null;
        try
        {
            if (Directory.Exists(path)) { problem = $"\"{Path.GetFileName(path)}\" is a folder. Attach the files inside it instead."; return null; }
            var info = new FileInfo(path);
            if (!info.Exists) { problem = $"\"{info.Name}\" doesn't exist any more."; return null; }
            if (info.Length > MaxBytes) { problem = $"\"{info.Name}\" is {FormatSize(info.Length)}. Files can be up to 30 MB."; return null; }
            return new PendingAttachment
            {
                DisplayName = info.Name,
                SourcePath = info.FullName,
                Size = info.Length,
                Thumbnail = ImageExtensions.Contains(info.Extension) ? LoadThumbnail(info.FullName) : null,
            };
        }
        catch (Exception ex) { problem = $"Couldn't attach \"{Path.GetFileName(path)}\": {ex.Message}"; return null; }
    }

    /// <summary>An image from the clipboard (e.g. a screenshot), kept as PNG until the message is sent.</summary>
    public static PendingAttachment? FromImage(BitmapSource image, out string? problem)
    {
        problem = null;
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            var bytes = ms.ToArray();
            if (bytes.Length > MaxBytes) { problem = $"The pasted image is {FormatSize(bytes.Length)}. Files can be up to 30 MB."; return null; }
            return new PendingAttachment
            {
                DisplayName = $"screenshot-{DateTime.Now:yyyyMMdd-HHmmss}.png",
                PngBytes = bytes,
                Size = bytes.Length,
                Thumbnail = Shrink(image),
            };
        }
        catch (Exception ex) { problem = $"Couldn't attach the pasted image: {ex.Message}"; return null; }
    }

    /// <summary>
    /// Copies the attachments into <paramref name="folder"/> (the workspace's attachments folder). Existing files
    /// are never overwritten (duplicates get " (2)", " (3)"...), and the originals are only read, never touched.
    /// Returns the new full paths, in order.
    /// </summary>
    public static List<string> SaveAll(IEnumerable<PendingAttachment> items, string folder)
    {
        Directory.CreateDirectory(folder);
        var saved = new List<string>();
        foreach (var a in items)
        {
            string name = SafeFileName(a.DisplayName);
            for (int n = 1; ; n++)
            {
                string candidate = Path.Combine(folder, n == 1 ? name
                    : $"{Path.GetFileNameWithoutExtension(name)} ({n}){Path.GetExtension(name)}");
                try
                {
                    // CreateNew: fails if the name is taken (even if another copy appeared meanwhile), never overwrites.
                    using var target = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write);
                    if (a.PngBytes != null) target.Write(a.PngBytes);
                    else
                    {
                        using var source = new FileStream(a.SourcePath!, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        if (source.Length > MaxBytes) throw new IOException($"\"{a.DisplayName}\" grew over 30 MB.");
                        source.CopyTo(target);
                    }
                    saved.Add(candidate);
                    break;
                }
                catch (IOException) when (File.Exists(candidate) && n < 1000) { /* name taken: try the next one */ }
            }
        }
        return saved;
    }

    /// <summary>What Claude receives: the user's text, then the full paths so it reads them with its own tools.</summary>
    public static string BuildMessage(string text, IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return text;
        var sb = new StringBuilder();
        if (text.Length > 0) sb.Append(text).Append("\n\n");
        sb.Append("Attached files:");
        foreach (var p in paths) sb.Append('\n').Append(p);
        return sb.ToString();
    }

    public static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        return clean.Length == 0 ? "attachment" : clean;
    }

    private static BitmapSource? LoadThumbnail(string path)
    {
        try
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.UriSource = new Uri(path);
            img.DecodePixelHeight = 64;            // small decode: thumbnails stay cheap in memory
            img.CacheOption = BitmapCacheOption.OnLoad; // don't keep the original file open
            img.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            img.EndInit();
            img.Freeze();
            return img;
        }
        catch { return null; } // unreadable image: show it as a normal file
    }

    private static BitmapSource Shrink(BitmapSource image)
    {
        double scale = Math.Min(1.0, 64.0 / Math.Max(1, image.PixelHeight));
        var t = new TransformedBitmap(image, new System.Windows.Media.ScaleTransform(scale, scale));
        t.Freeze();
        return t;
    }
}
