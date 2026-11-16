using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using WinRT;
using static OnRecord.Glass.Native;

namespace OnRecord.Glass;

/// <summary>
/// Real frosted acrylic behind a WPF window, with large antialiased rounded corners and a soft shadow.
///
/// How it works:
///  - The window has no Windows frame, no DWM corners, border or shadow, and a fully transparent
///    (non-layered) surface, so WPF content draws with per-pixel alpha.
///  - A Windows.UI.Composition visual tree is attached BEHIND the WPF content (DesktopWindowTarget,
///    isTopmost = false). It contains:
///      1. a LayerVisual with a DropShadow (the soft shadow), clipped to a rounded rectangle
///      2. a SpriteVisual painted with the system HostBackdropBrush (the blurred desktop and windows
///         behind us, exactly what Windows uses for acrylic), clipped to the same rounded rectangle.
///  - DWM composes all of this on the GPU, so dragging stays smooth. The host backdrop does not
///    switch off when the window loses focus.
/// The tint, dark base, border and all controls are ordinary WPF drawn on top.
/// </summary>
public sealed class GlassBackdrop
{
    private readonly Window _window;
    private readonly double _marginDip;
    private readonly double _radiusDip;

    private static IntPtr _dispatcherQueueController;
    private Compositor? _compositor;
    private DesktopWindowTarget? _target;
    private SpriteVisual? _glass;
    private SpriteVisual? _shadowShape;
    private LayerVisual? _shadowLayer;
    private CompositionRoundedRectangleGeometry? _glassGeometry;
    private CompositionRoundedRectangleGeometry? _shadowGeometry;

    /// <param name="marginDip">Transparent space around the glass for the shadow (and resize grabbing).</param>
    /// <param name="radiusDip">Corner radius of the glass.</param>
    public GlassBackdrop(Window window, double marginDip, double radiusDip)
    {
        _window = window;
        _marginDip = marginDip;
        _radiusDip = radiusDip;
        _window.SourceInitialized += (_, _) => Initialize();
        _window.SizeChanged += (_, _) => UpdateLayout();
        _window.DpiChanged += (_, _) => UpdateLayout();
    }

    private void Initialize()
    {
        var hwnd = new WindowInteropHelper(_window).Handle;
        var source = HwndSource.FromHwnd(hwnd);
        // Let WPF draw with real alpha instead of clearing to an opaque colour.
        source.CompositionTarget.BackgroundColor = Colors.Transparent;

        // Turn off everything DWM would normally draw for us: system backdrop, 8px corners and border.
        int none = DWMSBT_NONE;
        DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref none, sizeof(int));
        int noRound = DWMWCP_DONOTROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref noRound, sizeof(int));
        uint noBorder = DWMWA_COLOR_NONE;
        DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref noBorder, sizeof(uint));
        // Allow this window to use the host backdrop brush (acrylic).
        int on = 1;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_HOSTBACKDROPBRUSH, ref on, sizeof(int));
        // Extend the (invisible) frame over the whole window: transparent pixels stay transparent.
        var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);

        EnsureDispatcherQueue();
        _compositor = new Compositor();
        var interop = _compositor.As<ICompositorDesktopInterop>();
        interop.CreateDesktopWindowTarget(hwnd, false, out var targetPtr);
        _target = MarshalInspectable<DesktopWindowTarget>.FromAbi(targetPtr);
        Marshal.Release(targetPtr);

        var root = _compositor.CreateContainerVisual();
        root.RelativeSizeAdjustment = Vector2.One;

        // Shadow: a rounded dark shape inside a LayerVisual whose DropShadow follows the shape's alpha.
        _shadowGeometry = _compositor.CreateRoundedRectangleGeometry();
        _shadowShape = _compositor.CreateSpriteVisual();
        _shadowShape.Brush = _compositor.CreateColorBrush(Windows.UI.Color.FromArgb(255, 12, 12, 16));
        _shadowShape.Clip = _compositor.CreateGeometricClip(_shadowGeometry);
        _shadowLayer = _compositor.CreateLayerVisual();
        _shadowLayer.Children.InsertAtTop(_shadowShape);
        var shadow = _compositor.CreateDropShadow();
        shadow.Color = Windows.UI.Color.FromArgb(255, 0, 0, 0);
        shadow.Opacity = 0.55f;
        shadow.SourcePolicy = CompositionDropShadowSourcePolicy.InheritFromVisualContent;
        _shadowLayer.Shadow = shadow;

        // Glass: the system acrylic backdrop (blurred content behind the window).
        _glassGeometry = _compositor.CreateRoundedRectangleGeometry();
        _glass = _compositor.CreateSpriteVisual();
        _glass.Brush = _compositor.CreateHostBackdropBrush();
        _glass.Clip = _compositor.CreateGeometricClip(_glassGeometry);

        root.Children.InsertAtTop(_shadowLayer);
        root.Children.InsertAtTop(_glass);
        _target.Root = root;

        UpdateLayout();
    }

    private void UpdateLayout()
    {
        if (_compositor == null || _glass == null) return;
        var dpi = VisualTreeHelper.GetDpi(_window);
        float scale = (float)dpi.DpiScaleX;
        float w = (float)(_window.ActualWidth * scale);
        float h = (float)(_window.ActualHeight * scale);
        float m = (float)(_marginDip * scale);
        float r = (float)(_radiusDip * scale);
        var size = new Vector2(Math.Max(0, w - 2 * m), Math.Max(0, h - 2 * m));
        var offset = new Vector3(m, m, 0);

        foreach (var geo in new[] { _glassGeometry!, _shadowGeometry! })
        {
            geo.Size = size;
            geo.CornerRadius = new Vector2(r, r);
        }
        _glass.Offset = offset;
        _glass.Size = size;
        _shadowShape!.Size = size;
        _shadowLayer!.Offset = offset;
        _shadowLayer.Size = size;
        var shadow = (DropShadow)_shadowLayer.Shadow;
        shadow.BlurRadius = 26 * scale;
        shadow.Offset = new Vector3(0, 6 * scale, 0);
    }

    private static void EnsureDispatcherQueue()
    {
        if (_dispatcherQueueController != IntPtr.Zero) return;
        var options = new DispatcherQueueOptions
        {
            dwSize = Marshal.SizeOf<DispatcherQueueOptions>(),
            threadType = 2,     // current thread
            apartmentType = 2,  // STA
        };
        Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(options, out _dispatcherQueueController));
    }
}
