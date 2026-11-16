using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace OnRecord.Glass;

/// <summary>
/// Hosts WPF content in a per-pixel-transparent CHILD window.
///
/// Why: the acrylic backdrop is a Windows.UI.Composition visual attached to the top-level window.
/// Windows always draws that visual above the top-level window's own surface (where WPF would
/// normally render) but below child windows. So the UI lives in a child window that sits above
/// the glass, while its transparent pixels let the glass show through.
/// </summary>
public sealed class TransparentChildHost : HwndHost
{
    private const int WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000, WS_CLIPSIBLINGS = 0x04000000;
    private const int WM_NCHITTEST = 0x0084, HTTRANSPARENT = -1;

    private HwndSource? _source;
    private readonly FrameworkElement _content;

    /// <summary>Return true for a screen point (in physical pixels) that the parent should handle (resize edges).</summary>
    public Func<int, int, bool>? PassThroughHitTest { get; set; }

    public HwndSource? Source => _source;

    public TransparentChildHost(FrameworkElement content)
    {
        _content = content;
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        var p = new HwndSourceParameters("OnRecordUi")
        {
            ParentWindow = hwndParent.Handle,
            WindowStyle = WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS,
            UsesPerPixelTransparency = true,
            Width = 1,
            Height = 1,
        };
        _source = new HwndSource(p)
        {
            RootVisual = _content,
            SizeToContent = SizeToContent.Manual,
        };
        _source.CompositionTarget.BackgroundColor = Colors.Transparent;
        _source.AddHook(ChildHook);
        return new HandleRef(this, _source.Handle);
    }

    private IntPtr ChildHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_NCHITTEST && PassThroughHitTest != null)
        {
            long lp = lParam.ToInt64();
            int x = unchecked((short)(lp & 0xFFFF));
            int y = unchecked((short)((lp >> 16) & 0xFFFF));
            if (PassThroughHitTest(x, y))
            {
                handled = true;
                return HTTRANSPARENT; // let the parent window turn it into a resize edge
            }
        }
        return IntPtr.Zero;
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        _source?.Dispose();
        _source = null;
    }
}
