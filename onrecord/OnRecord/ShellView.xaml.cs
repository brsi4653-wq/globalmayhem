using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace OnRecord;

/// <summary>The visible UI inside the glass (top bar, chat, grip). Hosted in a transparent child window.</summary>
public partial class ShellView : UserControl
{
    private const double TintTopOpacity = 0.32;
    private const double TintBottomOpacity = 0.08;

    /// <summary>Mode colour, shared by the mode dot and the send button.</summary>
    private readonly SolidColorBrush _accent = new();

    public event Action? CloseRequested;
    public event Action? ExpandRequested;
    public event Action? DragRequested;
    public event Action? PinRequested;
    public event Action? NewChatRequested;
    public event Action? SettingsRequested;

    public string Mode { get; private set; } = Modes.Default;

    public ShellView()
    {
        InitializeComponent();
        ModeDot.Fill = _accent;
        Chat.SetAccentBrush(_accent);
        ApplyMode(Mode, animate: false);
        SizeChanged += (_, _) => UpdateSplitLayout();
        LayoutUpdated += OnLayoutUpdated;
    }

    private static readonly Duration TintDuration = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Switches the mode: pill text right away, tint/dot/send-button colour animated over ~500ms from
    /// whatever colour is showing now. Unknown modes keep the current mode.
    /// </summary>
    public void ApplyMode(string mode, bool animate = true)
    {
        if (Modes.IsKnown(mode)) Mode = mode.ToLowerInvariant();
        var c = Modes.ColorOf(Mode);
        ModeName.Text = Modes.DisplayName(Mode);
        SetColor(TintTop, GradientStop.ColorProperty, WithAlpha(c, TintTopOpacity), animate);
        SetColor(TintBottom, GradientStop.ColorProperty, WithAlpha(c, TintBottomOpacity), animate);
        SetColor(_accent, SolidColorBrush.ColorProperty, c, animate);
    }

    private static void SetColor(Animatable target, DependencyProperty property, Color to, bool animate)
    {
        if (!animate || !SystemParameters.ClientAreaAnimation)
        {
            target.BeginAnimation(property, null);
            target.SetValue(property, to);
            return;
        }
        var anim = new ColorAnimation(to, TintDuration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            FillBehavior = FillBehavior.HoldEnd,
        };
        target.BeginAnimation(property, anim, HandoffBehavior.SnapshotAndReplace);
    }

    // ---------------------------------------------------------------- artifact preview

    /// <summary>Glass width needed to show chat (~420) and the preview side by side.</summary>
    public const double SplitMinWidth = 900;
    public const double ChatWidthInSplit = 420;

    public event Action? PreviewCloseRequested;
    public event Action? PreviewOpenInBrowserRequested;
    public event Action? PreviewShowFileRequested;
    /// <summary>The web view placeholder moved or resized (or appeared/disappeared).</summary>
    public event Action? WebSlotChanged;

    public bool IsPreviewOpen => PreviewPanel.Visibility == Visibility.Visible;
    private Rect _lastSlot = Rect.Empty;

    /// <summary>Opens the panel for an artifact (web view over WebSlot) or one of the user's files.</summary>
    /// <param name="userFile">A file from a file card: buttons become "Open" / "Show in File Explorer".</param>
    public void ShowPreview(string fileName, bool userFile = false)
    {
        PreviewFileName.Text = fileName;
        PreviewFileName.ToolTip = fileName;
        PreviewOpenButton.Content = userFile ? "Open" : "Open in browser";
        PreviewShowFileButton.Content = userFile ? "Show in File Explorer" : "Show file";
        ClearNativePreview();
        PreviewPanel.Visibility = Visibility.Visible;
        UpdateSplitLayout();
    }

    /// <summary>Shows an already decoded image natively (the web view stays closed).</summary>
    public void ShowNativeImage(System.Windows.Media.Imaging.BitmapSource image)
    {
        PreviewImage.Source = image;
        PreviewImage.Visibility = Visibility.Visible;
        PreviewText.Visibility = Visibility.Collapsed;
        NativePreview.Visibility = Visibility.Visible;
    }

    /// <summary>Shows text natively, read-only (scripts are only ever shown, never run).</summary>
    public void ShowNativeText(string text)
    {
        PreviewText.Text = text;
        PreviewText.ScrollToHome();
        PreviewText.Visibility = Visibility.Visible;
        PreviewImage.Visibility = Visibility.Collapsed;
        NativePreview.Visibility = Visibility.Visible;
    }

    /// <summary>Drops the native preview's image/text so their memory can be freed.</summary>
    private void ClearNativePreview()
    {
        PreviewImage.Source = null;
        PreviewText.Clear();
        NativePreview.Visibility = Visibility.Collapsed;
    }

    public void HidePreview()
    {
        ClearNativePreview();
        PreviewPanel.Visibility = Visibility.Collapsed;
        UpdateSplitLayout();
    }

    /// <summary>Wide: chat left (~420px) + preview right. Narrow: preview covers the chat.</summary>
    private void UpdateSplitLayout()
    {
        bool open = IsPreviewOpen;
        bool wide = ActualWidth >= SplitMinWidth;
        if (open && wide)
        {
            ChatColumn.Width = new GridLength(ChatWidthInSplit);
            PreviewColumn.Width = new GridLength(1, GridUnitType.Star);
            Grid.SetColumn(PreviewPanel, 1);
            Grid.SetColumnSpan(PreviewPanel, 1);
            PreviewPanel.Margin = new Thickness(2, 0, 14, 14);
            Chat.Visibility = Visibility.Visible;
        }
        else if (open)
        {
            ChatColumn.Width = new GridLength(1, GridUnitType.Star);
            PreviewColumn.Width = new GridLength(0);
            Grid.SetColumn(PreviewPanel, 0);
            Grid.SetColumnSpan(PreviewPanel, 2);
            PreviewPanel.Margin = new Thickness(14, 0, 14, 14);
            Chat.Visibility = Visibility.Hidden;
        }
        else
        {
            ChatColumn.Width = new GridLength(1, GridUnitType.Star);
            PreviewColumn.Width = new GridLength(0);
            Chat.Visibility = Visibility.Visible;
        }
    }

    /// <summary>Where the web page should be, in DIPs relative to this view (Empty when hidden).</summary>
    public Rect GetWebSlotRect()
    {
        if (!IsPreviewOpen || !WebSlot.IsVisible || WebSlot.ActualWidth < 1) return Rect.Empty;
        var topLeft = WebSlot.TransformToAncestor(this).Transform(new Point(0, 0));
        return new Rect(topLeft, new Size(WebSlot.ActualWidth, WebSlot.ActualHeight));
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        var r = GetWebSlotRect();
        if (r != _lastSlot)
        {
            _lastSlot = r;
            WebSlotChanged?.Invoke();
        }
    }

    private void PreviewClose_Click(object sender, RoutedEventArgs e) => PreviewCloseRequested?.Invoke();
    private void PreviewOpenBrowser_Click(object sender, RoutedEventArgs e) => PreviewOpenInBrowserRequested?.Invoke();
    private void PreviewShowFile_Click(object sender, RoutedEventArgs e) => PreviewShowFileRequested?.Invoke();

    public void SetExpanded(bool expanded)
    {
        ExpandButton.Content = expanded ? "" : "";
        ExpandButton.ToolTip = expanded ? "Restore" : "Expand";
    }

    public void SetPinned(bool pinned)
    {
        PinButton.Content = pinned ? "" : ""; // PinFill / Pin
        PinButton.ToolTip = pinned ? "Unpin" : "Pin on top";
        if (pinned) PinButton.Background = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));
        else PinButton.ClearValue(BackgroundProperty);
    }

    /// <summary>Give keyboard focus to the UI's child window and the message box.</summary>
    public void FocusInput(HwndHost host)
    {
        if (host.Handle != IntPtr.Zero) SetFocus(host.Handle);
        Chat.FocusInput();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hwnd);

    private static Color WithAlpha(Color c, double opacity) =>
        Color.FromArgb((byte)Math.Round(opacity * 255), c.R, c.G, c.B);

    private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { ExpandRequested?.Invoke(); return; }
        DragRequested?.Invoke();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();
    private void ExpandButton_Click(object sender, RoutedEventArgs e) => ExpandRequested?.Invoke();
    private void PinButton_Click(object sender, RoutedEventArgs e) => PinRequested?.Invoke();
    private void NewChatButton_Click(object sender, RoutedEventArgs e) => NewChatRequested?.Invoke();
    private void SettingsButton_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();
}
