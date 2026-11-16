using System.Windows;
using System.Windows.Interop;
using static OnRecord.Glass.Native;

namespace OnRecord.Glass;

/// <summary>
/// Frameless-window behaviour: resize from every edge and corner (via WM_NCHITTEST, so Windows
/// does the actual resizing smoothly) and a bottom-right grip zone.
/// </summary>
public sealed class GlassChrome
{
    private readonly Window _window;
    private readonly double _marginDip;
    private const double InsideGrabDip = 6;   // how far inside the glass edge still grabs for resize
    private const double CornerGrabDip = 22;  // size of the diagonal corner zones
    private const double GripDip = 22;        // bottom-right grip square inside the glass

    public GlassChrome(Window window, double marginDip)
    {
        _window = window;
        _marginDip = marginDip;
        _window.SourceInitialized += (_, _) =>
            HwndSource.FromHwnd(new WindowInteropHelper(_window).Handle).AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_NCHITTEST:
                int hit = HitTest(hwnd, lParam);
                if (hit != HTCLIENT)
                {
                    handled = true;
                    return hit;
                }
                break;

            case WM_NCLBUTTONDOWN:
                // The window has no resizable frame (that would bring back the system border and
                // shadow), so start Windows' own resize loop directly. SC_SIZE + WMSZ_* direction.
                int code = wParam.ToInt32();
                if (code >= HTLEFT && code <= HTBOTTOMRIGHT)
                {
                    SendMessage(hwnd, WM_SYSCOMMAND, (IntPtr)(SC_SIZE + (code - 9)), lParam);
                    handled = true;
                }
                break;

            case WM_SETCURSOR:
                int cursor = (lParam.ToInt64() & 0xFFFF) switch
                {
                    HTLEFT or HTRIGHT => IDC_SIZEWE,
                    HTTOP or HTBOTTOM => IDC_SIZENS,
                    HTTOPLEFT or HTBOTTOMRIGHT => IDC_SIZENWSE,
                    HTTOPRIGHT or HTBOTTOMLEFT => IDC_SIZENESW,
                    _ => 0,
                };
                if (cursor != 0)
                {
                    SetCursor(LoadCursor(IntPtr.Zero, cursor));
                    handled = true;
                    return 1;
                }
                break;
        }
        return IntPtr.Zero;
    }

    /// <summary>True if a screen point (physical pixels) is on a resize edge, corner or the grip.</summary>
    public bool IsResizeZone(int screenX, int screenY)
    {
        var hwnd = new WindowInteropHelper(_window).Handle;
        return hwnd != IntPtr.Zero && HitTest(hwnd, screenX, screenY) != HTCLIENT;
    }

    private int HitTest(IntPtr hwnd, IntPtr lParam)
    {
        long lp = lParam.ToInt64();
        return HitTest(hwnd, unchecked((short)(lp & 0xFFFF)), unchecked((short)((lp >> 16) & 0xFFFF)));
    }

    private int HitTest(IntPtr hwnd, int x, int y)
    {
        if (_window.WindowState != WindowState.Normal) return HTCLIENT;
        GetWindowRect(hwnd, out var rc);

        double scale = System.Windows.Media.VisualTreeHelper.GetDpi(_window).DpiScaleX;
        double edge = (_marginDip + InsideGrabDip) * scale;
        double corner = (_marginDip + CornerGrabDip) * scale;
        double grip = (_marginDip + GripDip) * scale;

        int lx = x - rc.Left, ly = y - rc.Top;
        int w = rc.Right - rc.Left, h = rc.Bottom - rc.Top;

        bool left = lx < edge, right = lx >= w - edge, top = ly < edge, bottom = ly >= h - edge;
        bool nearLeft = lx < corner, nearRight = lx >= w - corner, nearTop = ly < corner, nearBottom = ly >= h - corner;

        if (lx >= w - grip && ly >= h - grip) return HTBOTTOMRIGHT;
        if ((top && nearLeft) || (left && nearTop)) return HTTOPLEFT;
        if ((top && nearRight) || (right && nearTop)) return HTTOPRIGHT;
        if ((bottom && nearLeft) || (left && nearBottom)) return HTBOTTOMLEFT;
        if ((bottom && nearRight) || (right && nearBottom)) return HTBOTTOMRIGHT;
        if (left) return HTLEFT;
        if (right) return HTRIGHT;
        if (top) return HTTOP;
        if (bottom) return HTBOTTOM;
        return HTCLIENT;
    }
}
