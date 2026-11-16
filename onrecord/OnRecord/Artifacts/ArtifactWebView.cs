using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace OnRecord.Artifacts;

/// <summary>
/// The only WebView2 in the app. Created when an artifact opens, fully closed (browser processes
/// released) when the preview closes.
///
/// Artifact pages are UNTRUSTED (Claude wrote them, possibly after reading web pages):
///  - served from a virtual https host mapped to the artifacts folder only, never as file:// (so they can't
///    load or show other local files), with their own WebView2 profile folder;
///  - no host objects, no web messaging to the app, no dev tools, no downloads, no permission prompts
///    (camera, location...), no external protocol launches;
///  - the preview never navigates away from the artifact; links clicked by the user open in the default
///    browser (http/https/mailto only), anything else is dropped.
///
/// It is created with the low-level CoreWebView2Controller as a child of the TOP-LEVEL window, not
/// inside the WPF UI: the UI lives in a per-pixel-transparent (layered) child window, and Windows can't
/// show ordinary child windows inside layered windows. Its bounds are kept over a placeholder in the
/// preview panel by the caller.
/// </summary>
public sealed class ArtifactWebView : IDisposable
{
    /// <summary>Reserved .example host: never a real site, resolved inside WebView2 only.</summary>
    public const string VirtualHost = "onrecord-artifacts.example";

    private CoreWebView2Controller? _controller;
    private int _generation;   // guards against a close happening while creation is still in flight
    private Rect _boundsPx;
    private bool _visible = true;
    private string? _currentUri;

    public bool IsOpen => _controller != null;

    /// <summary>The URL an artifact file is shown at (only files directly in the artifacts folder).</summary>
    public static string UriFor(string fileName) => $"https://{VirtualHost}/{Uri.EscapeDataString(fileName)}";

    /// <summary>Opens (or re-navigates) the preview to an artifact already validated by <see cref="ArtifactName"/>.</summary>
    public async Task OpenAsync(IntPtr parentHwnd, string filePath)
    {
        _currentUri = UriFor(Path.GetFileName(filePath));
        if (_controller != null)
        {
            _controller.CoreWebView2.Navigate(_currentUri);
            return;
        }

        int gen = ++_generation;
        var env = await CoreWebView2Environment.CreateAsync(null, AppPaths.PreviewWebViewData);
        var controller = await env.CreateCoreWebView2ControllerAsync(parentHwnd);
        if (gen != _generation)
        {
            controller.Close(); // closed while we were creating it
            return;
        }
        _controller = controller;
        controller.DefaultBackgroundColor = System.Drawing.Color.White;
        var web = controller.CoreWebView2;

        var s = web.Settings;
        s.AreHostObjectsAllowed = false;      // no app objects exposed to the page
        s.IsWebMessageEnabled = false;        // no chrome.webview.postMessage to the app
        s.AreDevToolsEnabled = false;
        s.IsStatusBarEnabled = false;
        s.IsPasswordAutosaveEnabled = false;
        s.IsGeneralAutofillEnabled = false;
        s.AreDefaultContextMenusEnabled = true;

        // Serve ONLY the artifacts folder, under a virtual https origin.
        web.SetVirtualHostNameToFolderMapping(VirtualHost, AppPaths.Artifacts, CoreWebView2HostResourceAccessKind.Deny);

        web.NavigationStarting += OnNavigationStarting;
        web.FrameNavigationStarting += OnFrameNavigationStarting;
        web.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;                 // never a popup window inside the app
            if (e.IsUserInitiated) OpenLinkExternally(e.Uri);
        };
        web.DownloadStarting += (_, e) => { e.Cancel = true; e.Handled = true; };
        web.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
        web.LaunchingExternalUriScheme += (_, e) => e.Cancel = true;
        // Nothing from the page is ever acted on; in test mode, log if anything arrives at all.
        web.WebMessageReceived += (_, _) => Trace("WEB MESSAGE RECEIVED");

        ApplyBounds();
        controller.IsVisible = _visible;
        web.Navigate(_currentUri);
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (string.Equals(e.Uri, _currentUri, StringComparison.Ordinal)) return; // the artifact itself (and reloads)
        e.Cancel = true;                                                          // the preview never goes anywhere else
        if (e.IsUserInitiated) OpenLinkExternally(e.Uri);
        Trace($"blocked navigation to {e.Uri} (user={e.IsUserInitiated})");
    }

    private static void OnFrameNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        // Embedded frames may show web content (like an image or a video embed) but never local files or other schemes.
        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var u) && (u.Scheme is "https" or "http" || e.Uri == "about:blank"
                || u.Scheme == "about" || (u.Scheme == "data")))
            return;
        e.Cancel = true;
        Trace($"blocked frame navigation to {e.Uri}");
    }

    /// <summary>Opens a link from an artifact in the user's default browser. Only http, https and mailto.</summary>
    public static void OpenLinkExternally(string? uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https" or "mailto")) return;
        if (Environment.GetEnvironmentVariable("ONRECORD_TEST_NO_EXTERNAL") == "1") { Trace($"would open externally: {u.AbsoluteUri}"); return; }
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true }); }
        catch { /* no browser registered */ }
    }

    /// <summary>"Open in browser" for the current artifact: only an existing .html file in the artifacts folder.</summary>
    public static void OpenFileInBrowser(string validatedPath)
    {
        if (!ArtifactName.IsSafeExistingFile(validatedPath)
            || ArtifactName.Resolve(Path.GetFileName(validatedPath), AppPaths.Artifacts, out _) is not { } path
            || !string.Equals(path, validatedPath, StringComparison.OrdinalIgnoreCase)) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
        catch { /* nothing registered for .html */ }
    }

    // Diagnostics for tests only (ONRECORD_TRACE_NAV=1): logs blocked navigations.
    private static void Trace(string line)
    {
        if (Environment.GetEnvironmentVariable("ONRECORD_TRACE_NAV") != "1") return;
        try { File.AppendAllText(Path.Combine(AppPaths.Logs, "nav-trace.log"), line + "\n"); } catch { }
    }

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

    /// <summary>Closes the web view and releases its browser processes.</summary>
    public void Close()
    {
        _generation++;
        _currentUri = null;
        var c = _controller;
        _controller = null;
        c?.Close();
    }

    public void Dispose() => Close();
}
