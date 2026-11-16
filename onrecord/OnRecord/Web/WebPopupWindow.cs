using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;

namespace OnRecord.Web;

/// <summary>
/// A sign-in pop-up (OAuth "Sign in with..."): a small ordinary window owned by OnRecord's window, using the web
/// pages' environment so the login lands in the same profile. Closed with the page, the panel, or when OnRecord hides.
/// </summary>
public sealed class WebPopupWindow : Window
{
    private CoreWebView2Controller? _controller;
    private bool _closed;

    public WebPopupWindow(Window owner, CoreWebView2WindowFeatures features)
    {
        Owner = owner;
        Title = "Sign in";
        ShowInTaskbar = false;
        ShowActivated = true;
        Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x1B));
        Width = Math.Clamp(features.HasSize ? features.Width : 500, 360, 900);
        Height = Math.Clamp(features.HasSize ? features.Height : 640, 360, 900);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SourceInitialized += (_, _) =>
        {
            int on = 1; // dark title bar
            DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref on, sizeof(int));
        };
        SizeChanged += (_, _) => FitController();
    }

    /// <summary>Creates the page's controller in this window. Null if the window was closed meanwhile.</summary>
    public async Task<CoreWebView2?> AttachAsync(CoreWebView2Environment env)
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        var controller = await env.CreateCoreWebView2ControllerAsync(hwnd);
        if (_closed) { controller.Close(); return null; }
        _controller = controller;
        controller.DefaultBackgroundColor = System.Drawing.Color.White;
        FitController();
        var core = controller.CoreWebView2;
        core.WindowCloseRequested += (_, _) => Dispatcher.BeginInvoke(Close); // window.close() at the end of a sign-in
        core.DocumentTitleChanged += (_, _) => Title = string.IsNullOrWhiteSpace(core.DocumentTitle) ? "Sign in" : core.DocumentTitle;
        return core;
    }

    private void FitController()
    {
        if (_controller == null) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !GetClientRect(hwnd, out var r)) return;
        _controller.Bounds = new System.Drawing.Rectangle(0, 0, r.Right - r.Left, r.Bottom - r.Top);
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        var c = _controller;
        _controller = null;
        c?.Close();
        base.OnClosed(e);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
