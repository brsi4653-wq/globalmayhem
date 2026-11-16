using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;

namespace OnRecord.Web;

/// <summary>
/// Web pages from "WEB: url" lines, shown in the side panel (same place as artifacts, one at a time).
///
/// - Its OWN WebView2 environment and persistent profile (<see cref="AppPaths.WebProfile"/>), never shared with the
///   artifact preview, so logins survive restarts and artifacts never see website cookies. The environment object
///   lives for the whole app run so new pages start fast; only the controller is created and closed per page.
/// - Fresh page every time: each WEB line closes the current controller (its renderer ends) and creates a new one.
/// - Pages are ordinary websites the user asked for: autofill and password saving work, but there are no host objects
///   or web messages to the app, no external protocol launches, only http/https in the main frame, and downloads
///   always land in the Downloads folder.
/// - "New tab" links load in the same panel. Sized pop-ups (OAuth "Sign in with...") get a small owned window from
///   the same environment (<see cref="WebPopupWindow"/>), closed together with the page.
///
/// Like the artifact preview, the controller is a child of the TOP-LEVEL window (WebView2 can't show inside the
/// layered UI child); the caller keeps its bounds over the panel's placeholder.
/// </summary>
public sealed class WebPageView
{
    private static CoreWebView2Environment? s_env;
    private static TaskCompletionSource? s_envExited;

    private CoreWebView2Controller? _controller;
    private readonly List<WebPopupWindow> _popups = new();
    private int _generation;   // guards against a close (or a newer page) while creation is still in flight
    private Rect _boundsPx;
    private bool _visible = true;

    public bool IsOpen => _controller != null;
    /// <summary>The page's current address (follows navigation), or null.</summary>
    public string? CurrentUrl => _controller?.CoreWebView2.Source is { } s && WebTargets.IsWebUrl(s) ? s : _openedUrl;
    public bool CanGoBack => _controller?.CoreWebView2.CanGoBack == true;
    private string? _openedUrl;

    /// <summary>Title changed (or the page changed). UI thread.</summary>
    public event Action<string>? TitleChanged;
    /// <summary>Back became possible or impossible. UI thread.</summary>
    public event Action<bool>? HistoryChanged;

    /// <summary>Shows a brand-new page for <paramref name="url"/> (already made safe by <see cref="WebTargets.ToUrl"/>).</summary>
    public async Task OpenAsync(Window owner, string url)
    {
        int gen = ++_generation;
        _openedUrl = url;
        ClosePopups();
        var hwnd = new WindowInteropHelper(owner).Handle;
        var controller = await CreateControllerAsync(hwnd);
        if (gen != _generation)
        {
            controller.Close(); // closed, or another page was asked for, while we were creating this one
            return;
        }

        // The new page exists: now end the old one (its renderer goes away).
        var old = _controller;
        _controller = controller;
        old?.Close();

        controller.DefaultBackgroundColor = System.Drawing.Color.White;
        var web = controller.CoreWebView2;
        Configure(web, inPopup: false, owner);
        web.DocumentTitleChanged += (_, _) => { if (_controller == controller) TitleChanged?.Invoke(web.DocumentTitle); };
        web.HistoryChanged += (_, _) => { if (_controller == controller) HistoryChanged?.Invoke(web.CanGoBack); };
        ApplyBounds();
        controller.IsVisible = _visible;
        HistoryChanged?.Invoke(false);
        Trace($"open {Describe(url)} (browser pid {web.BrowserProcessId})");
        web.Navigate(url);
    }

    public void GoBack() { if (_controller?.CoreWebView2 is { CanGoBack: true } w) w.GoBack(); }
    public void Reload() => _controller?.CoreWebView2.Reload();

    /// <summary>The shared environment, created on first use. A new one only if the old browser process has ended.</summary>
    private static async Task<CoreWebView2Controller> CreateControllerAsync(IntPtr parent)
    {
        var env = await GetEnvironmentAsync();
        try
        {
            return await env.CreateCoreWebView2ControllerAsync(parent);
        }
        catch (Exception ex) when (ex is not WebView2RuntimeNotFoundException)
        {
            // Every page was closed, so the browser process was shutting down just as we asked it for a new page.
            // Let it finish (it holds the profile folder), then start again with a new environment.
            Trace($"controller failed ({ex.GetType().Name} {ex.HResult:X8}), restarting the environment");
            var exited = s_envExited;
            if (exited != null) await Task.WhenAny(exited.Task, Task.Delay(5000));
            if (ReferenceEquals(s_env, env)) s_env = null;
            env = await GetEnvironmentAsync();
            return await env.CreateCoreWebView2ControllerAsync(parent);
        }
    }

    private static async Task<CoreWebView2Environment> GetEnvironmentAsync()
    {
        if (s_env != null) return s_env;
        Directory.CreateDirectory(AppPaths.WebProfile);
        var env = await CoreWebView2Environment.CreateAsync(null, AppPaths.WebProfile, new CoreWebView2EnvironmentOptions());
        if (s_env != null) return s_env; // another caller won the race
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        env.BrowserProcessExited += (_, e) => { Trace($"browser process exited ({e.BrowserProcessExitKind})"); exited.TrySetResult(); };
        s_env = env;
        s_envExited = exited;
        return env;
    }

    /// <summary>Same rules for the panel page and its pop-ups.</summary>
    private void Configure(CoreWebView2 web, bool inPopup, Window owner)
    {
        var s = web.Settings;
        s.AreHostObjectsAllowed = false;   // no app objects exposed to the page
        s.IsWebMessageEnabled = false;     // no chrome.webview.postMessage to the app
        s.AreDevToolsEnabled = false;
        s.IsStatusBarEnabled = true;
        s.IsPasswordAutosaveEnabled = true;
        s.IsGeneralAutofillEnabled = true;
        s.AreDefaultContextMenusEnabled = true;
        try { web.Profile.DefaultDownloadFolderPath = DownloadsFolder(); } catch { }

        web.NavigationStarting += (_, e) =>
        {
            if (IsPageUri(e.Uri)) return;
            e.Cancel = true;
            Trace($"blocked navigation to {Scheme(e.Uri)}:");
        };
        web.FrameNavigationStarting += (_, e) =>
        {
            if (IsPageUri(e.Uri) || e.Uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase)
                || e.Uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || e.Uri.StartsWith("blob:", StringComparison.OrdinalIgnoreCase)) return;
            e.Cancel = true;
            Trace($"blocked frame navigation to {Scheme(e.Uri)}:");
        };
        // mailto:, ms-settings:, steam:, ... never start another program from a web page.
        web.LaunchingExternalUriScheme += (_, e) => { e.Cancel = true; Trace($"blocked external protocol {Scheme(e.Uri)}:"); };
        web.DownloadStarting += (_, e) =>
        {
            // Whatever the page or a "Save as" dialog asked for, the file goes into Downloads.
            string name = Path.GetFileName(e.ResultFilePath);
            if (string.IsNullOrWhiteSpace(name)) name = "download";
            e.ResultFilePath = Path.Combine(DownloadsFolder(), name);
        };
        web.NewWindowRequested += async (_, e) =>
        {
            if (!inPopup && e.WindowFeatures.HasSize && (IsPageUri(e.Uri) || e.Uri.Length == 0))
            {
                await OpenPopupAsync(e, owner);
                return;
            }
            // No tabs: "new tab" links (and pop-ups from pop-ups) load in the same view.
            e.Handled = true;
            if (WebTargets.IsWebUrl(e.Uri)) web.Navigate(e.Uri);
        };
    }

    private async Task OpenPopupAsync(CoreWebView2NewWindowRequestedEventArgs e, Window owner)
    {
        int gen = _generation;
        var deferral = e.GetDeferral();
        WebPopupWindow? popup = null;
        try
        {
            popup = new WebPopupWindow(owner, e.WindowFeatures);
            _popups.Add(popup);
            popup.Closed += (_, _) => _popups.Remove(popup);
            popup.Show();
            var env = await GetEnvironmentAsync();
            var core = await popup.AttachAsync(env);
            if (gen != _generation || core == null) { popup.Close(); e.Handled = true; return; }
            Configure(core, inPopup: true, owner);
            e.NewWindow = core;   // keeps window.opener, so the sign-in can report back to the page
            e.Handled = true;
            Trace("pop-up opened");
        }
        catch (Exception ex)
        {
            Trace($"pop-up failed: {ex.Message}");
            popup?.Close();
            e.Handled = true;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private static bool IsPageUri(string uri) =>
        WebTargets.IsWebUrl(uri) || uri.Equals("about:blank", StringComparison.OrdinalIgnoreCase);

    private static string Scheme(string uri) { int i = uri.IndexOf(':'); return i > 0 && i < 40 ? uri[..i] : "?"; }

    private static string Describe(string url) => Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : "?";

    /// <summary>Position in physical pixels relative to the parent window's client area.</summary>
    public void SetBounds(Rect boundsPx)
    {
        _boundsPx = boundsPx;
        ApplyBounds();
    }

    public void SetVisible(bool visible)
    {
        _visible = visible;
        if (_controller != null) _controller.IsVisible = visible;
    }

    private void ApplyBounds()
    {
        if (_controller == null || _boundsPx.IsEmpty) return;
        _controller.Bounds = new System.Drawing.Rectangle(
            (int)Math.Round(_boundsPx.X), (int)Math.Round(_boundsPx.Y),
            (int)Math.Max(0, Math.Round(_boundsPx.Width)), (int)Math.Max(0, Math.Round(_boundsPx.Height)));
    }

    private void ClosePopups()
    {
        foreach (var p in _popups.ToArray()) { try { p.Close(); } catch { } }
        _popups.Clear();
    }

    /// <summary>Closes the page and its pop-ups. With no page left, the web browser process ends by itself.</summary>
    public void Close()
    {
        _generation++;
        _openedUrl = null;
        ClosePopups();
        var c = _controller;
        _controller = null;
        if (c != null) { c.Close(); Trace("closed"); }
    }

    /// <summary>The user's real Downloads folder (it can be moved, so never guess %USERPROFILE%\Downloads).</summary>
    private static string DownloadsFolder()
    {
        var id = new Guid("374DE290-123F-4565-9164-39C4925E467B");
        if (SHGetKnownFolderPath(id, 0, IntPtr.Zero, out IntPtr p) == 0)
        {
            try { return Marshal.PtrToStringUni(p)!; }
            finally { Marshal.FreeCoTaskMem(p); }
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(in Guid id, uint flags, IntPtr token, out IntPtr path);

    // Diagnostics for tests only (ONRECORD_TRACE_WEB=1): events, hosts and schemes, never page content or full URLs.
    internal static void Trace(string line)
    {
        if (Environment.GetEnvironmentVariable("ONRECORD_TRACE_WEB") != "1") return;
        try { File.AppendAllText(Path.Combine(AppPaths.Logs, "web-trace.log"), $"{DateTime.Now:HH:mm:ss.fff} {line}\n"); } catch { }
    }
}
