using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OnRecord.Platform;

/// <summary>
/// Thumbnails from Windows' own thumbnail system (IShellItemImageFactory, the same one File Explorer uses), so
/// images, videos, PDFs and Office documents get real previews and everything else gets its file-type icon.
/// One background STA thread handles one request at a time, and images are requested small, to keep RAM low.
/// </summary>
public static class ShellThumbnails
{
    public sealed record Result(BitmapSource Image, bool IsIcon);

    private static readonly BlockingCollection<(string Path, int Size, TaskCompletionSource<Result?> Done)> Queue = new();
    private static Thread? _worker;
    private static readonly object Gate = new();

    /// <summary>A thumbnail about <paramref name="sizePx"/> pixels square (frozen, usable on any thread), or null.</summary>
    public static Task<Result?> GetAsync(string path, int sizePx)
    {
        var tcs = new TaskCompletionSource<Result?>(TaskCreationOptions.RunContinuationsAsynchronously);
        EnsureWorker();
        Queue.Add((path, Math.Clamp(sizePx, 16, 512), tcs));
        return tcs.Task;
    }

    private static void EnsureWorker()
    {
        lock (Gate)
        {
            if (_worker != null) return;
            _worker = new Thread(() =>
            {
                foreach (var (path, size, done) in Queue.GetConsumingEnumerable())
                {
                    try { done.TrySetResult(Load(path, size)); }
                    catch { done.TrySetResult(null); }
                }
            }) { IsBackground = true, Name = "OnRecord thumbnails", Priority = ThreadPriority.BelowNormal };
            _worker.SetApartmentState(ApartmentState.STA); // shell extensions expect STA
            _worker.Start();
        }
    }

    private static Result? Load(string path, int size)
    {
        var iid = typeof(IShellItemImageFactory).GUID;
        int hr = SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var factory);
        if (hr != 0 || factory == null) { Trace($"create {hr:X8} {path}"); return null; }
        try
        {
            var sz = new SIZE { cx = size, cy = size };
            // A real thumbnail first (photos, videos, PDFs, documents); otherwise the file-type icon.
            hr = factory.GetImage(sz, SIIGBF_THUMBNAILONLY, out var hbm);
            Trace($"thumb {hr:X8} {size}px {path}");
            if (hr == 0 && hbm != IntPtr.Zero)
                return ToBitmap(hbm) is { } t ? new Result(t, false) : null;
            hr = factory.GetImage(sz, SIIGBF_ICONONLY, out hbm);
            Trace($"icon {hr:X8} {path}");
            if (hr == 0 && hbm != IntPtr.Zero)
                return ToBitmap(hbm) is { } i ? new Result(i, true) : null;
            return null;
        }
        finally { Marshal.ReleaseComObject(factory); }
    }

    /// <summary>Copies an HBITMAP into a frozen 32-bit BitmapSource (top-down) and frees the HBITMAP.</summary>
    private static BitmapSource? ToBitmap(IntPtr hbm)
    {
        try
        {
            if (GetObject(hbm, Marshal.SizeOf<BITMAP>(), out BITMAP bm) == 0 || bm.bmWidth <= 0 || bm.bmHeight <= 0) return null;
            int w = bm.bmWidth, h = Math.Abs(bm.bmHeight), stride = w * 4;
            var pixels = new byte[stride * h];
            var info = new BITMAPINFOHEADER { biSize = Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
            IntPtr dc = CreateCompatibleDC(IntPtr.Zero);
            try { if (GetDIBits(dc, hbm, 0, (uint)h, pixels, ref info, 0) == 0) return null; }
            finally { DeleteDC(dc); }

            // Some thumbnails carry no alpha at all (every alpha byte 0): treat them as opaque.
            bool anyAlpha = false;
            for (int i = 3; i < pixels.Length; i += 4) if (pixels[i] != 0) { anyAlpha = true; break; }
            if (!anyAlpha) for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;

            var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
            bmp.Freeze();
            return bmp;
        }
        finally { DeleteObject(hbm); }
    }

    // Diagnostics for tests only (ONRECORD_TRACE_THUMBS=1): logs what Windows returned for each thumbnail.
    private static readonly bool TraceEnabled = Environment.GetEnvironmentVariable("ONRECORD_TRACE_THUMBS") == "1";
    private static void Trace(string line)
    {
        if (!TraceEnabled) return;
        try { System.IO.File.AppendAllText(System.IO.Path.Combine(AppPaths.Logs, "thumbs-trace.log"), line + "\n"); } catch { }
    }

    // ---------------------------------------------------------------- interop

    private const int SIIGBF_ICONONLY = 0x4, SIIGBF_THUMBNAILONLY = 0x8;

    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        // Room for the colour table GetDIBits may write for BI_RGB 32bpp (none, but keep the struct generous).
        public int pad0, pad1, pad2, pad3;
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory? factory);

    [DllImport("gdi32.dll")] private static extern int GetObject(IntPtr h, int size, out BITMAP bm);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr h);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines, [Out] byte[] bits, ref BITMAPINFOHEADER info, uint usage);
}
