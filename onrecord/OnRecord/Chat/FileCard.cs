using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OnRecord.Platform;

namespace OnRecord.Chat;

/// <summary>
/// A card for one "OPEN: path" line: thumbnail, name, folder, size and date, plus "Open" and
/// "Show in File Explorer". Nothing happens until the user clicks. The thumbnail is only loaded once the card
/// is actually scrolled into view, and requested small.
/// </summary>
public sealed class FileCard : Border
{
    public const double ThumbSize = 64;
    private static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public string Path { get; }
    private readonly bool _isFolder;
    private readonly Func<Window?> _owner;
    private readonly Func<string, bool> _preview;
    private readonly Border _thumb;
    private readonly TextBlock _message;
    private ScrollViewer? _scroller;
    private bool _thumbRequested;

    /// <param name="preview">Shows the file in the side panel; returns false if it can't be previewed there.</param>
    public FileCard(string path, bool isFolder, Func<Window?> owner, Func<string, bool> preview)
    {
        Path = path;
        _isFolder = isFolder;
        _owner = owner;
        _preview = preview;

        CornerRadius = new CornerRadius(16);
        Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
        BorderBrush = new SolidColorBrush(Color.FromArgb(0x2A, 0xFF, 0xFF, 0xFF));
        BorderThickness = new Thickness(1);
        Padding = new Thickness(10);
        Margin = new Thickness(0, 4, 0, 4);
        MinWidth = 320;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ThumbSize) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Thumbnail (a glyph until the real one arrives, and if Windows has none).
        _thumb = new Border
        {
            Width = ThumbSize, Height = ThumbSize, CornerRadius = new CornerRadius(10), VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)), Cursor = Cursors.Hand, ClipToBounds = true,
            ToolTip = isFolder ? "Open folder" : FileTargets.KindOf(path) != PreviewKind.None ? "Preview" : "Open",
            Child = Glyph(isFolder ? "\uE8B7" : "\uE8A5", 26),
        };
        _thumb.MouseLeftButtonUp += (_, e) => { e.Handled = true; ThumbnailClicked(); };
        grid.Children.Add(_thumb);

        var info = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(info, 1);
        string name = System.IO.Path.GetFileName(path);
        if (name.Length == 0) name = path; // a drive root like C:\
        info.Children.Add(new TextBlock
        {
            Text = name, FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = new SolidColorBrush(Color.FromArgb(0xF5, 0xFF, 0xFF, 0xFF)), ToolTip = path,
        });
        string folder = System.IO.Path.GetDirectoryName(path) ?? "";
        if (folder.Length > 0)
            info.Children.Add(new TextBlock
            {
                Text = folder, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0),
                Foreground = new SolidColorBrush(Color.FromArgb(0xA6, 0xFF, 0xFF, 0xFF)), ToolTip = folder,
            });
        info.Children.Add(new TextBlock
        {
            Text = Details(path, isFolder), FontSize = 12, Margin = new Thickness(0, 1, 0, 0),
            Foreground = new SolidColorBrush(Color.FromArgb(0xA6, 0xFF, 0xFF, 0xFF)), TextTrimming = TextTrimming.CharacterEllipsis,
        });

        var buttons = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        var open = CardButton(isFolder ? "\uE838" : "\uE8A7", isFolder ? "Open folder" : "Open");
        open.Click += (_, _) => Show(FileTargets.Open(Path, _owner()));
        var show = CardButton("\uEC50", "Show in File Explorer");
        show.Click += (_, _) => Show(FileTargets.ShowInExplorer(Path));
        buttons.Children.Add(open);
        buttons.Children.Add(show);
        info.Children.Add(buttons);

        _message = new TextBlock
        {
            FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed,
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x9B, 0x9B)),
        };
        info.Children.Add(_message);
        grid.Children.Add(info);
        Child = grid;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private static string Details(string path, bool isFolder)
    {
        try
        {
            if (isFolder) return $"Folder · modified {Date(Directory.GetLastWriteTime(path))}";
            var fi = new FileInfo(path);
            return $"{FileTargets.FormatSize(fi.Length)} · modified {Date(fi.LastWriteTime)}";
        }
        catch { return isFolder ? "Folder" : "File"; }
    }

    private static string Date(DateTime t) =>
        t.Date == DateTime.Today ? $"today {t:t}" : t.Date == DateTime.Today.AddDays(-1) ? $"yesterday {t:t}" : $"{t:d MMM yyyy}, {t:t}";

    private void ThumbnailClicked()
    {
        if (!FileTargets.Exists(Path, out _)) { Show("It isn't there any more. It may have been moved, renamed or deleted."); return; }
        if (!_isFolder && FileTargets.KindOf(Path) != PreviewKind.None && _preview(Path)) { Show(null); return; }
        Show(FileTargets.Open(Path, _owner())); // anything else: its default program (programs still ask first)
    }

    /// <summary>Shows a problem under the buttons (null hides it). A missing file also dims the card.</summary>
    private void Show(string? message)
    {
        _message.Text = message ?? "";
        _message.Visibility = message == null ? Visibility.Collapsed : Visibility.Visible;
        Opacity = message != null && !FileTargets.Exists(Path, out _) ? 0.75 : 1;
    }

    // ---------------------------------------------------------------- lazy thumbnail

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _scroller = FindScroller();
        if (_scroller != null) _scroller.ScrollChanged += OnScrollChanged;
        // Layout may not be final yet (the card was just added while streaming): check once it is.
        Dispatcher.BeginInvoke(CheckOnScreen, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_scroller != null) _scroller.ScrollChanged -= OnScrollChanged;
        _scroller = null;
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e) => CheckOnScreen();

    private void CheckOnScreen()
    {
        if (_thumbRequested || !IsVisible || _scroller == null) return;
        try
        {
            var r = _thumb.TransformToAncestor(_scroller).TransformBounds(new Rect(0, 0, _thumb.ActualWidth, _thumb.ActualHeight));
            if (r.Bottom < -40 || r.Top > _scroller.ViewportHeight + 40) return; // not on screen yet
        }
        catch (InvalidOperationException) { return; }
        _thumbRequested = true;
        if (_scroller != null) _scroller.ScrollChanged -= OnScrollChanged;
        _ = LoadThumbnailAsync();
    }

    private async Task LoadThumbnailAsync()
    {
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var result = await ShellThumbnails.GetAsync(Path, (int)Math.Ceiling(ThumbSize * scale));
        if (result == null) return; // keep the glyph
        _thumb.Child = new Image
        {
            Source = result.Image,
            // Photos and video frames fill the square; icons stay whole and smaller.
            Stretch = result.IsIcon ? Stretch.Uniform : Stretch.UniformToFill,
            Margin = result.IsIcon ? new Thickness(10) : new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        RenderOptions.SetBitmapScalingMode(_thumb.Child, BitmapScalingMode.HighQuality);
    }

    private ScrollViewer? FindScroller()
    {
        DependencyObject? d = this;
        while ((d = VisualTreeHelper.GetParent(d)) != null)
            if (d is ScrollViewer sv) return sv;
        return null;
    }

    // ---------------------------------------------------------------- look

    private static TextBlock Glyph(string glyph, double size) => new()
    {
        Text = glyph, FontFamily = IconFont, FontSize = size,
        Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
    };

    private static Button CardButton(string glyph, string text)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock { Text = glyph, FontFamily = IconFont, FontSize = 12, Margin = new Thickness(0, 1, 7, 0), VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(new TextBlock { Text = text, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center });
        var b = new Button
        {
            Content = content, Cursor = Cursors.Hand, Focusable = false, Margin = new Thickness(0, 0, 6, 0),
            Template = AssistantMessageView.ChipTemplate(new Thickness(10, 4, 12, 5)),
            Foreground = new SolidColorBrush(Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF)),
        };
        System.Windows.Automation.AutomationProperties.SetName(b, text);
        return b;
    }
}
