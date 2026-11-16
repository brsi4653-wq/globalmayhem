using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using OnRecord.Artifacts;
using OnRecord.Glass;
using Microsoft.Web.WebView2.Core;
using static OnRecord.Glass.Native;

namespace OnRecord;

public partial class MainWindow : Window
{
    /// <summary>Transparent space around the glass for the shadow. Must match HostSlot's Margin.</summary>
    public const double ShadowMargin = 24;
    public const double CornerRadius = 30;
    private const int WM_EXITSIZEMOVE = 0x0232;

    public ShellView Shell { get; } = new();
    private readonly AppSettings _settings;
    private readonly GlassChrome _chrome;
    private readonly TransparentChildHost _host;
    private Rect? _boundsBeforeExpand;
    private readonly Chat.ChatController _chat;

    public MainWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();
        _ = new GlassBackdrop(this, ShadowMargin, CornerRadius);
        _chrome = new GlassChrome(this, ShadowMargin);

        _host = new TransparentChildHost(Shell)
        {
            // Resize edges inside the glass belong to the parent window.
            PassThroughHitTest = (x, y) => _chrome.IsResizeZone(x, y),
        };
        HostSlot.Children.Add(_host);

        Shell.CloseRequested += HidePopup;
        Shell.ExpandRequested += ToggleExpand;
        Shell.DragRequested += BeginDrag;
        Shell.PinRequested += () => SetPinned(!_settings.PinOnTop);

        _chat = new Chat.ChatController(Shell.Chat, Dispatcher,
            model: () => _settings.Model,             // applies to each new session
            allowReadAnywhere: () => _settings.AllowReadAnywhere,
            onMode: mode => Shell.ApplyMode(mode),    // animated tint change
            onArtifact: name => _ = OpenArtifactAsync(name),
            previewFile: PreviewFile);                // file cards: bigger preview in the side panel
        Shell.NewChatRequested += () =>
        {
            ClosePreview();
            Shell.ApplyMode(Modes.Default);
            _chat.NewChat();
            Shell.FocusInput(_host);
        };

        Shell.PreviewCloseRequested += () => { ClosePreview(); Shell.FocusInput(_host); };
        Shell.PreviewOpenInBrowserRequested += () =>
        {
            if (_previewUserFile != null) { ReportFileProblem(Chat.FileTargets.Open(_previewUserFile, this)); return; }
            if (_previewPath != null) ArtifactWebView.OpenFileInBrowser(_previewPath);
        };
        Shell.PreviewShowFileRequested += () =>
        {
            if (_previewUserFile != null) { ReportFileProblem(Chat.FileTargets.ShowInExplorer(_previewUserFile)); return; }
            // _previewPath was validated (plain .html name inside the artifacts folder, so no quotes in it).
            if (_previewPath != null && ArtifactName.IsSafeExistingFile(_previewPath))
                System.Diagnostics.Process.Start(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
                    $"/select,\"{_previewPath}\"");
        };
        Shell.WebSlotChanged += UpdateWebBounds;
        Shell.Chat.DialogOwner = () => this;
        Shell.SettingsRequested += () => SettingsRequested?.Invoke();
        // Coming back to the window (click, alt-tab): put the cursor back in the message box.
        Activated += (_, _) => Shell.FocusInput(_host);

        RestoreBounds_();
        SetPinned(_settings.PinOnTop, save: false);

        SourceInitialized += (_, _) =>
        {
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle).AddHook(WndProc);
            if (_settings.Expanded) ToggleExpand();
        };
    }

    // ---------------------------------------------------------------- show / hide

    /// <summary>Alt+Space / tray: show and focus. If already open, just focus.</summary>
    public void ShowPopup()
    {
        bool wasHidden = !IsVisible;
        if (wasHidden) Show();
        Activate();
        var hwnd = new WindowInteropHelper(this).Handle;
        SetForegroundWindow(hwnd);
        Shell.FocusInput(_host);
        if (wasHidden)
        {
            // Every open after being closed = a brand new Claude session with no earlier context.
            Shell.ApplyMode(Modes.Default);
            _chat.NewChat();
        }
    }

    /// <summary>X: hide to the tray and end the Claude session (and close any artifact).</summary>
    public void HidePopup()
    {
        Hide();
        EndChat();      // also closes the preview, which restores the width if we widened the window for it
        SaveBounds();
    }

    /// <summary>Set by Quit. Until then, any close request (Alt+F4 etc.) just hides to the tray like X.</summary>
    public bool AllowClose { get; set; }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            HidePopup();
        }
        base.OnClosing(e);
    }

    public event Action? SettingsRequested;

    /// <summary>After the colour map changed: re-apply the current mode's colours (animated).</summary>
    public void RefreshMode() => Shell.ApplyMode(Modes.IsKnown(Shell.Mode) ? Shell.Mode : Modes.Default);

    public void EndChat()
    {
        _chat.EndSession();
        ClosePreview();
        Shell.Chat.Clear();
        Shell.ApplyMode(Modes.Default, animate: false);
    }

    // ---------------------------------------------------------------- artifacts

    private readonly ArtifactWebView _web = new();
    private string? _previewPath;
    /// <summary>Left/width before we widened the window to fit the preview (restored when it closes).</summary>
    private (double Left, double Width, double GrownWidth)? _boundsBeforePreview;

    private async Task OpenArtifactAsync(string name)
    {
        // Only a plain .html file directly inside the artifacts folder; anything else is refused, never "fixed up".
        if (ArtifactName.Resolve(name, AppPaths.Artifacts, out var problem) is not { } path)
        {
            Shell.Chat.AddNotice(Chat.NoticeKind.Blocked, $"Didn't open the artifact \"{Shorten(name)}\": {problem}.");
            return;
        }
        string fileName = System.IO.Path.GetFileName(path);
        if (!ArtifactName.IsSafeExistingFile(path))
        {
            Shell.Chat.AddNotice(Chat.NoticeKind.Error, $"Claude mentioned the artifact \"{fileName}\", but it isn't in {AppPaths.Artifacts}.");
            return;
        }

        _previewPath = path;
        _previewUserFile = null;
        MakeRoomForPreview();
        Shell.ShowPreview(fileName);
        try
        {
            await _web.OpenAsync(new WindowInteropHelper(this).Handle, path);
            // The web view is a sibling of the UI's child window; keep the UI at the bottom so the page shows.
            SetWindowPos(_host.Handle, new IntPtr(1) /* HWND_BOTTOM */, 0, 0, 0, 0, 0x0013 /* NOSIZE|NOMOVE|NOACTIVATE */);
            UpdateWebBounds();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            ClosePreview();
            Shell.Chat.AddNotice(Chat.NoticeKind.Error, "The WebView2 runtime isn't installed, so artifacts can't be previewed. Use the file in " + AppPaths.Artifacts);
        }
        catch (Exception ex)
        {
            ClosePreview();
            Shell.Chat.AddNotice(Chat.NoticeKind.Error, $"Couldn't open the preview: {ex.Message}");
        }
    }

    // ---------------------------------------------------------------- previews of the user's own files (file cards)

    /// <summary>The user's file shown in the panel (from a file card), or null.</summary>
    private string? _previewUserFile;

    /// <summary>Longest side an image is decoded at for the preview (keeps big photos from using lots of RAM).</summary>
    private const int MaxPreviewImagePx = 2000;

    /// <summary>
    /// A file card's thumbnail was clicked: images and text are shown natively, PDFs in the sandboxed web view.
    /// Returns false if this file can't be previewed here (the card then opens it in its default program).
    /// </summary>
    private bool PreviewFile(string path)
    {
        var kind = Chat.FileTargets.KindOf(path);
        if (kind == Chat.PreviewKind.None || !System.IO.File.Exists(path)) return false;

        string? text = null;
        if (kind == Chat.PreviewKind.Text && (text = ReadTextForPreview(path)) == null) return false; // binary after all

        _web.Close();            // never keep an old page around behind a native preview or a new PDF
        _previewPath = null;
        _previewUserFile = path;
        MakeRoomForPreview();
        Shell.ShowPreview(System.IO.Path.GetFileName(path), userFile: true);

        switch (kind)
        {
            case Chat.PreviewKind.Text: Shell.ShowNativeText(text!); break;
            case Chat.PreviewKind.Image: _ = ShowImageAsync(path); break;
            case Chat.PreviewKind.Pdf: _ = ShowPdfAsync(path); break;
        }
        return true;
    }

    private async Task ShowImageAsync(string path)
    {
        var image = await Task.Run(() => DecodeImage(path));
        if (_previewUserFile != path) return; // closed or replaced meanwhile
        if (image == null)
        {
            // A format WPF can't decode itself (or a broken file): Windows' own large thumbnail, if there is one.
            var thumb = await Platform.ShellThumbnails.GetAsync(path, 1024);
            if (_previewUserFile != path) return;
            image = thumb is { IsIcon: false } ? thumb.Image : null;
        }
        if (image != null) { Shell.ShowNativeImage(image); return; }
        ClosePreview();
        ReportFileProblem(Chat.FileTargets.Open(path, this)); // let its default program try
    }

    /// <summary>Decodes an image for the panel, at most <see cref="MaxPreviewImagePx"/> on its longest side.</summary>
    private static BitmapSource? DecodeImage(string path)
    {
        try
        {
            int w, h;
            using (var probe = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var frame = BitmapFrame.Create(probe, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                w = frame.PixelWidth; h = frame.PixelHeight;
            }
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path);
            bmp.CacheOption = BitmapCacheOption.OnLoad;          // the file is closed right after
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            if (w >= h && w > MaxPreviewImagePx) bmp.DecodePixelWidth = MaxPreviewImagePx;
            else if (h > w && h > MaxPreviewImagePx) bmp.DecodePixelHeight = MaxPreviewImagePx;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    /// <summary>Up to 1 MB of a text file, or null if it looks binary.</summary>
    private static string? ReadTextForPreview(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new byte[(int)Math.Min(fs.Length, Chat.FileTargets.MaxTextPreviewBytes)];
            int read = 0, n;
            while (read < buffer.Length && (n = fs.Read(buffer, read, buffer.Length - read)) > 0) read += n;
            bool utf16 = read >= 2 && ((buffer[0] == 0xFF && buffer[1] == 0xFE) || (buffer[0] == 0xFE && buffer[1] == 0xFF));
            if (!utf16 && Array.IndexOf(buffer, (byte)0, 0, Math.Min(read, 8192)) >= 0) return null;
            using var reader = new StreamReader(new MemoryStream(buffer, 0, read), System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            string text = reader.ReadToEnd();
            if (fs.Length > read) text += $"\n\n… Only the first {Chat.FileTargets.FormatSize(read)} is shown. Use Open to see all of it.";
            return text;
        }
        catch { return null; }
    }

    private async Task ShowPdfAsync(string path)
    {
        try
        {
            await _web.OpenPdfAsync(new WindowInteropHelper(this).Handle, path);
            if (_previewUserFile != path) return;
            // The web view is a sibling of the UI's child window; keep the UI at the bottom so the page shows.
            SetWindowPos(_host.Handle, new IntPtr(1) /* HWND_BOTTOM */, 0, 0, 0, 0, 0x0013);
            UpdateWebBounds();
        }
        catch (Exception ex)
        {
            ClosePreview();
            Shell.Chat.AddNotice(Chat.NoticeKind.Error, ex is WebView2RuntimeNotFoundException
                ? "The WebView2 runtime isn't installed, so PDFs can't be previewed here. Use Open instead."
                : $"Couldn't preview the PDF: {ex.Message}");
        }
    }

    private void ReportFileProblem(string? problem)
    {
        if (problem != null) Shell.Chat.AddNotice(Chat.NoticeKind.Error, problem);
    }

    private static string Shorten(string s)
    {
        s = new string(s.Where(c => !char.IsControl(c)).ToArray());
        return s.Length > 60 ? s[..60] + "…" : s;
    }

    public void ClosePreview()
    {
        _web.Close(); // disposes the WebView2 and releases its browser processes
        _previewPath = null;
        _previewUserFile = null;
        if (Shell.IsPreviewOpen) Shell.HidePreview();
        // Only undo our widening if the user hasn't resized the window themselves since.
        if (_boundsBeforePreview is { } b && _boundsBeforeExpand == null && Math.Abs(Width - b.GrownWidth) < 1)
        {
            Left = b.Left;
            Width = b.Width;
        }
        _boundsBeforePreview = null;
    }

    /// <summary>If the window is too narrow for chat + preview side by side, widen it (within the screen).</summary>
    private void MakeRoomForPreview()
    {
        if (Shell.IsPreviewOpen || _boundsBeforeExpand != null) return;
        double glassWidth = ActualWidth - 2 * ShadowMargin;
        if (glassWidth >= ShellView.SplitMinWidth) return;

        var work = CurrentWorkArea();
        const double targetGlass = 1060, gap = 12;
        double maxGlass = work.Width - 2 * gap;
        if (maxGlass < ShellView.SplitMinWidth) return; // screen too small: the preview covers the chat instead

        double newWidth = Math.Min(targetGlass, maxGlass) + 2 * ShadowMargin;
        double newLeft = Left;
        double rightLimit = work.Right - gap + ShadowMargin;
        if (newLeft + newWidth > rightLimit) newLeft = rightLimit - newWidth;
        newLeft = Math.Max(newLeft, work.Left + gap - ShadowMargin);

        _boundsBeforePreview = (Left, Width, newWidth);
        Left = newLeft;
        Width = newWidth;
    }

    private Rect CurrentWorkArea()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(MonitorFromWindow(hwnd, 2 /* nearest */), ref info);
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        return new Rect(info.rcWork.Left / scale, info.rcWork.Top / scale,
            (info.rcWork.Right - info.rcWork.Left) / scale, (info.rcWork.Bottom - info.rcWork.Top) / scale);
    }

    /// <summary>Keeps the web view exactly over the placeholder in the preview panel.</summary>
    private void UpdateWebBounds()
    {
        var slot = Shell.GetWebSlotRect();
        if (slot.IsEmpty)
        {
            _web.SetVisible(false);
            return;
        }
        double s = VisualTreeHelper.GetDpi(this).DpiScaleX;
        // The UI child window sits ShadowMargin inside the top-level window.
        _web.SetBounds(new Rect((ShadowMargin + slot.X) * s, (ShadowMargin + slot.Y) * s, slot.Width * s, slot.Height * s));
        _web.SetVisible(true);
    }

    public void SetPinned(bool pinned, bool save = true)
    {
        _settings.PinOnTop = pinned;
        Topmost = pinned;
        Shell.SetPinned(pinned);
        if (save) _settings.Save();
    }

    // ---------------------------------------------------------------- bounds

    private void RestoreBounds_()
    {
        Width = Math.Max(MinWidth, _settings.Width);
        Height = Math.Max(MinHeight, _settings.Height);
        bool saved = !double.IsNaN(_settings.Left) && !double.IsNaN(_settings.Top);
        if (saved && IsOnScreen(_settings.Left, _settings.Top, Width))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = _settings.Left;
            Top = _settings.Top;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    /// <summary>True if the top bar would be reachable on the current desktop (monitors can change).</summary>
    private static bool IsOnScreen(double left, double top, double width)
    {
        var desktop = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var topBar = new Rect(left + ShadowMargin, top + ShadowMargin, Math.Max(1, width - 2 * ShadowMargin), 40);
        var visible = Rect.Intersect(desktop, topBar);
        return !visible.IsEmpty && visible.Width >= 120 && visible.Height >= 20;
    }

    public void SaveBounds()
    {
        var normal = _boundsBeforeExpand ?? new Rect(Left, Top, ActualWidth > 0 ? ActualWidth : Width, ActualHeight > 0 ? ActualHeight : Height);
        if (double.IsNaN(normal.Left) || normal.Width < 1) return;
        _settings.Left = normal.Left;
        _settings.Top = normal.Top;
        _settings.Width = normal.Width;
        _settings.Height = normal.Height;
        _settings.Expanded = _boundsBeforeExpand != null;
        _settings.Save();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_EXITSIZEMOVE) SaveBounds(); // finished a drag or resize
        return IntPtr.Zero;
    }

    // ---------------------------------------------------------------- drag / expand

    private void BeginDrag()
    {
        // Same as Window.DragMove: hand the drag to Windows' own move loop (smooth, no lag).
        const int SC_MOUSEMOVE = 0xF012;
        ReleaseCapture();
        SendMessage(new WindowInteropHelper(this).Handle, WM_SYSCOMMAND, (IntPtr)SC_MOUSEMOVE, IntPtr.Zero);
    }

    /// <summary>Expand to fill most of the current monitor's work area, or restore.</summary>
    private void ToggleExpand()
    {
        if (_boundsBeforeExpand is Rect old)
        {
            _boundsBeforeExpand = null;
            Left = old.Left; Top = old.Top; Width = old.Width; Height = old.Height;
            Shell.SetExpanded(false);
            SaveBounds();
            return;
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(MonitorFromWindow(hwnd, 2 /* nearest */), ref info);
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var work = new Rect(info.rcWork.Left / scale, info.rcWork.Top / scale,
            (info.rcWork.Right - info.rcWork.Left) / scale, (info.rcWork.Bottom - info.rcWork.Top) / scale);

        _boundsBeforeExpand = new Rect(Left, Top, ActualWidth > 0 ? ActualWidth : Width, ActualHeight > 0 ? ActualHeight : Height);
        const double gap = 12; // visible gap between the glass and the screen edge
        Left = work.Left + gap - ShadowMargin;
        Top = work.Top + gap - ShadowMargin;
        Width = work.Width - 2 * (gap - ShadowMargin);
        Height = work.Height - 2 * (gap - ShadowMargin);
        Shell.SetExpanded(true);
        SaveBounds();
    }

    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
}
