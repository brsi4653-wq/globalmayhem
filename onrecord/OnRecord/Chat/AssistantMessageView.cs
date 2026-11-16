using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace OnRecord.Chat;

public enum NoticeKind { Blocked, Error, Info }

/// <summary>
/// One Claude reply: plain text on the left (native Markdown), a small status line while it's working
/// ("Searching the web…"), and notices such as blocked actions.
/// </summary>
public sealed class AssistantMessageView : StackPanel
{
    private readonly RichTextBox _body;
    private readonly TextBlock _status;
    private readonly StackPanel _notices;
    private readonly StackPanel _cards;
    private readonly HashSet<string> _cardPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _renderTimer;
    private string _markdown = "";
    private string _rendered = "\0";

    public AssistantMessageView()
    {
        _body = new RichTextBox
        {
            IsReadOnly = true,
            IsDocumentEnabled = true,          // links clickable
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Foreground = new SolidColorBrush(Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF)),
            SelectionBrush = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)),
            CaretBrush = Brushes.Transparent,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(-2, 0, 0, 0),
        };
        _status = new TextBlock
        {
            FontSize = 13,
            FontStyle = FontStyles.Italic,
            Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
            Margin = new Thickness(0, 2, 0, 2),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Visibility = Visibility.Collapsed,
        };
        _notices = new StackPanel();
        // Left-aligned and as wide as the widest card, so stacked cards line up.
        _cards = new StackPanel { Margin = new Thickness(0, 2, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, MaxWidth = 520 };
        Children.Add(_body);
        Children.Add(_cards);   // file cards from OPEN lines, under the reply text
        Children.Add(_status);
        Children.Add(_notices);

        // Re-render the Markdown at most ~25 times a second while streaming.
        _renderTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(40) };
        _renderTimer.Tick += (_, _) => { _renderTimer.Stop(); RenderNow(); };
    }

    public bool IsEmpty => _markdown.Trim().Length == 0 && _notices.Children.Count == 0 && _cards.Children.Count == 0;

    /// <summary>Adds a file card (once per path per reply). Several cards stack under each other.</summary>
    public void AddFileCard(FileCard card)
    {
        if (_cardPaths.Add(card.Path)) _cards.Children.Add(card);
    }

    public bool HasCardFor(string path) => _cardPaths.Contains(path);

    public void SetMarkdown(string markdown, bool immediate = false)
    {
        _markdown = markdown;
        if (immediate) { _renderTimer.Stop(); RenderNow(); }
        else if (!_renderTimer.IsEnabled) _renderTimer.Start();
    }

    public void SetStatus(string? text)
    {
        _status.Text = text ?? "";
        _status.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    public void AddNotice(NoticeKind kind, string text) => _notices.Children.Add(CreateNotice(kind, text));

    /// <summary>A small clickable chip for an artifact, so it can be reopened after the preview is closed.</summary>
    public void AddArtifactChip(string fileName, Action open)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock
        {
            Text = "",
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 13,
            Margin = new Thickness(0, 1, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        content.Children.Add(new TextBlock { Text = fileName, FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center });
        var chip = new Button
        {
            Content = content,
            Cursor = System.Windows.Input.Cursors.Hand,
            Focusable = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 6, 0, 4),
            ToolTip = "Open preview",
            Template = ChipTemplate(new Thickness(12, 6, 14, 6)),
            Foreground = new SolidColorBrush(Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF)),
        };
        chip.Click += (_, _) => open();
        _notices.Children.Add(chip);
    }

    internal static ControlTemplate ChipTemplate(Thickness padding)
    {
        var t = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border), "Bg");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(14));
        border.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF)));
        border.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.PaddingProperty, padding);
        border.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        t.VisualTree = border;
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(0x3A, 0xFF, 0xFF, 0xFF)), "Bg"));
        t.Triggers.Add(hover);
        return t;
    }

    private void RenderNow()
    {
        if (_markdown == _rendered) return;
        _rendered = _markdown;
        if (_markdown.Trim().Length == 0)
        {
            _body.Visibility = Visibility.Collapsed;
            return;
        }
        _body.Document = MarkdownRenderer.Render(_markdown);
        _body.Visibility = Visibility.Visible;
    }

    public static Border CreateNotice(NoticeKind kind, string text)
    {
        var (accent, glyph, title) = kind switch
        {
            NoticeKind.Blocked => (Color.FromRgb(0xFF, 0xB8, 0x4D), "", "Blocked"),
            NoticeKind.Error => (Color.FromRgb(0xFF, 0x6B, 0x6B), "", "Error"),
            _ => (Color.FromRgb(0x9C, 0xC8, 0xFF), "", ""),
        };
        var row = new DockPanel();
        var icon = new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 14,
            Foreground = new SolidColorBrush(accent),
            Margin = new Thickness(0, 2, 10, 0),
            VerticalAlignment = VerticalAlignment.Top,
        };
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);
        var body = new TextBox
        {
            Text = title.Length > 0 ? $"{title}: {text}" : text,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF)),
            FontSize = 13.5,
            Template = ChatPanel.PlainTextBoxTemplate(),
        };
        row.Children.Add(body);
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x26, accent.R, accent.G, accent.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, accent.R, accent.G, accent.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 8, 12, 9),
            Margin = new Thickness(0, 6, 0, 6),
            Child = row,
        };
    }
}
