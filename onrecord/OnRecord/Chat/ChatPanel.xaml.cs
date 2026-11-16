using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Automation;
using System.Windows.Media;

namespace OnRecord.Chat;

/// <summary>The chat: message list + input bar. Pure UI; ChatController drives it.</summary>
public partial class ChatPanel : UserControl
{
    private bool _stickToBottom = true;

    /// <summary>The user pressed Enter / Send with this text and these attachments (either may be empty, not both).</summary>
    public event Action<string, IReadOnlyList<PendingAttachment>>? Submitted;

    /// <summary>The window that owns dialogs opened from the chat (the file picker).</summary>
    public Func<Window?>? DialogOwner { get; set; }

    private readonly List<PendingAttachment> _pending = new();

    public ChatPanel()
    {
        InitializeComponent();
        Messages.SizeChanged += (_, _) => UpdateBubbleWidths();
        // Ctrl+V: files and images from the clipboard become attachments; text pastes as usual.
        CommandManager.AddPreviewCanExecuteHandler(InputBox, (_, e) =>
        {
            if (e.Command == ApplicationCommands.Paste && ClipboardHasAttachment()) { e.CanExecute = true; e.Handled = true; }
        });
        CommandManager.AddPreviewExecutedHandler(InputBox, (_, e) =>
        {
            if (e.Command == ApplicationCommands.Paste && PasteAttachments()) e.Handled = true;
        });
    }

    /// <summary>The send button and other accents use this brush (it follows the mode colour).</summary>
    public void SetAccentBrush(Brush brush) => SendButton.Background = brush;

    public void FocusInput()
    {
        InputBox.Focus();
        Keyboard.Focus(InputBox);
        InputBox.CaretIndex = InputBox.Text.Length;
    }

    public void Clear()
    {
        Messages.Children.Clear();
        EmptyHint.Visibility = Visibility.Visible;
        _pending.Clear();      // new chat / closed popup: unsent attachments go too
        RefreshChips();
        ShowAttachError(null);
        _stickToBottom = true;
    }

    public void AddUserMessage(string text, IReadOnlyList<string>? fileNames = null)
    {
        var content = new StackPanel();
        if (text.Length > 0)
            content.Children.Add(new TextBox
            {
                Text = text,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = new SolidColorBrush(Color.FromArgb(0xF5, 0xFF, 0xFF, 0xFF)),
                SelectionBrush = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)),
                FontSize = 14.5,
                Padding = new Thickness(0),
                Template = PlainTextBoxTemplate(),
            });
        if (fileNames is { Count: > 0 })
        {
            var chips = new WrapPanel { Margin = new Thickness(0, text.Length > 0 ? 6 : 0, 0, 0) };
            foreach (var name in fileNames) chips.Children.Add(FileChip(name, null, null));
            content.Children.Add(chips);
        }
        var bubble = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            CornerRadius = new CornerRadius(18),
            Background = new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 8, 14, 9),
            Margin = new Thickness(40, 10, 0, 10),
            Tag = "user",
            Child = content,
        };
        Add(bubble);
        _stickToBottom = true;
        UpdateBubbleWidths();
    }

    public AssistantMessageView BeginAssistantMessage()
    {
        var view = new AssistantMessageView { Margin = new Thickness(0, 6, 0, 10) };
        Add(view);
        _stickToBottom = true;
        return view;
    }

    public void Remove(UIElement element)
    {
        Messages.Children.Remove(element);
        if (Messages.Children.Count == 0) EmptyHint.Visibility = Visibility.Visible;
    }

    /// <summary>A notice outside any reply (e.g. Claude couldn't start).</summary>
    public void AddNotice(NoticeKind kind, string text) => Add(AssistantMessageView.CreateNotice(kind, text));

    private void Add(UIElement element)
    {
        EmptyHint.Visibility = Visibility.Collapsed;
        Messages.Children.Add(element);
    }

    private void UpdateBubbleWidths()
    {
        double max = Math.Max(120, Messages.ActualWidth * 0.82);
        foreach (var child in Messages.Children)
            if (child is Border { Tag: "user" } b) b.MaxWidth = max;
    }

    // ------------------------------------------------------------- scrolling: follow new content unless the user scrolled up

    private void Scroller_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentHeightChange == 0 && e.ViewportHeightChange == 0)
            _stickToBottom = Scroller.VerticalOffset >= Scroller.ScrollableHeight - 24; // user scrolled
        else if (_stickToBottom)
            Scroller.ScrollToEnd();
    }

    // ------------------------------------------------------------- input

    private void InputBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            // Shift+Enter: new line
            int caret = InputBox.CaretIndex;
            InputBox.Text = InputBox.Text.Insert(caret, Environment.NewLine);
            InputBox.CaretIndex = caret + Environment.NewLine.Length;
        }
        else
        {
            Submit();
        }
        e.Handled = true;
    }

    private void InputBox_TextChanged(object sender, TextChangedEventArgs e) =>
        Placeholder.Visibility = InputBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void SendButton_Click(object sender, RoutedEventArgs e) => Submit();

    private void Submit()
    {
        string text = InputBox.Text.Trim();
        if (text.Length == 0 && _pending.Count == 0) return;
        var attachments = _pending.ToList();
        InputBox.Clear();
        _pending.Clear();
        RefreshChips();
        ShowAttachError(null);
        Submitted?.Invoke(text, attachments);
        FocusInput();
    }

    // ------------------------------------------------------------- attachments

    private void AttachButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Attach files",
            Multiselect = true,
            CheckFileExists = true,
            Filter = "All files|*.*|Images|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp|PDF documents|*.pdf|Text and code|*.txt;*.md;*.csv;*.json;*.xml;*.log;*.cs;*.py;*.js;*.ts;*.html;*.css",
        };
        bool? ok = DialogOwner?.Invoke() is { } owner ? dialog.ShowDialog(owner) : dialog.ShowDialog();
        if (ok == true) AddFiles(dialog.FileNames);
        FocusInput();
    }

    /// <summary>Adds files to the input bar. Problems (too big, folders) are shown above the bar.</summary>
    public void AddFiles(IEnumerable<string> paths)
    {
        var problems = new List<string>();
        foreach (var path in paths)
        {
            if (_pending.Any(p => string.Equals(p.SourcePath, path, StringComparison.OrdinalIgnoreCase))) continue;
            if (Attachments.FromFile(path, out var problem) is { } a) _pending.Add(a);
            else if (problem != null) problems.Add(problem);
        }
        RefreshChips();
        ShowAttachError(problems.Count > 0 ? string.Join("\n", problems) : null);
    }

    private static bool ClipboardHasAttachment()
    {
        try { return !Clipboard.ContainsText() && (Clipboard.ContainsFileDropList() || Clipboard.ContainsImage() || Clipboard.ContainsData("PNG")); }
        catch { return false; } // clipboard busy
    }

    /// <summary>Ctrl+V: attach copied files or a copied image. Returns false for normal text pastes.</summary>
    private bool PasteAttachments()
    {
        try
        {
            if (Clipboard.ContainsText()) return false;
            if (Clipboard.ContainsFileDropList())
            {
                AddFiles(Clipboard.GetFileDropList().Cast<string>());
                return true;
            }
            System.Windows.Media.Imaging.BitmapSource? image = null;
            // Prefer the PNG format (keeps transparency; used by the Snipping Tool), else the standard bitmap.
            if (Clipboard.GetData("PNG") is System.IO.MemoryStream png)
                image = System.Windows.Media.Imaging.BitmapFrame.Create(png, System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
            image ??= Clipboard.GetImage();
            if (image == null) return false;
            if (Attachments.FromImage(image, out var problem) is { } a) { _pending.Add(a); RefreshChips(); ShowAttachError(null); }
            else ShowAttachError(problem);
            return true;
        }
        catch (Exception ex)
        {
            ShowAttachError($"Couldn't paste from the clipboard: {ex.Message}");
            return true;
        }
    }

    private void Panel_DragOver(object sender, DragEventArgs e)
    {
        bool files = e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = files ? DragDropEffects.Copy : DragDropEffects.None;
        DropHint.Visibility = files ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    private void Panel_DragLeave(object sender, DragEventArgs e) => DropHint.Visibility = Visibility.Collapsed;

    private void Panel_Drop(object sender, DragEventArgs e)
    {
        DropHint.Visibility = Visibility.Collapsed;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files) AddFiles(files);
        e.Handled = true;
        FocusInput();
    }

    private void RefreshChips()
    {
        AttachmentChips.Children.Clear();
        foreach (var a in _pending)
        {
            var item = a;
            AttachmentChips.Children.Add(FileChip(a.DisplayName, a.Thumbnail, () => { _pending.Remove(item); RefreshChips(); FocusInput(); }));
        }
        Placeholder.Text = _pending.Count > 0 ? "Add a message or press Enter to send" : "Ask Claude…";
    }

    private void ShowAttachError(string? text)
    {
        AttachError.Text = text ?? "";
        AttachError.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>A small chip: image preview or file icon, the name, and (in the input bar) an x to remove it.</summary>
    private static Border FileChip(string name, System.Windows.Media.Imaging.BitmapSource? thumbnail, Action? onRemove)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        if (thumbnail != null)
            row.Children.Add(new Border
            {
                Width = 28, Height = 28, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 7, 0),
                Background = new ImageBrush(thumbnail) { Stretch = Stretch.UniformToFill },
            });
        else
            row.Children.Add(new TextBlock
            {
                Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)), VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 7, 0),
            });
        row.Children.Add(new TextBlock
        {
            Text = name, MaxWidth = 170, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12.5,
            Foreground = new SolidColorBrush(Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF)), VerticalAlignment = VerticalAlignment.Center,
            ToolTip = name,
        });
        if (onRemove != null)
        {
            var x = new Button
            {
                Content = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 9,
                Width = 20, Height = 20, Margin = new Thickness(6, 0, 0, 0), Cursor = Cursors.Hand, Focusable = false,
                Foreground = Brushes.White, ToolTip = "Remove", VerticalAlignment = VerticalAlignment.Center,
                Template = RoundButtonTemplate(),
            };
            AutomationProperties.SetName(x, "Remove " + name);
            x.Click += (_, _) => onRemove();
            row.Children.Add(x);
        }
        return new Border
        {
            CornerRadius = new CornerRadius(12), Padding = new Thickness(thumbnail != null ? 4 : 9, 4, onRemove != null ? 4 : 10, 4),
            Margin = new Thickness(0, 0, 6, 6),
            Background = new SolidColorBrush(Color.FromArgb(0x2A, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1),
            Child = row,
        };
    }

    private static ControlTemplate RoundButtonTemplate()
    {
        var t = new ControlTemplate(typeof(Button));
        var grid = new FrameworkElementFactory(typeof(Grid));
        var circle = new FrameworkElementFactory(typeof(System.Windows.Shapes.Ellipse));
        circle.SetValue(System.Windows.Shapes.Shape.FillProperty, new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        grid.AppendChild(circle); grid.AppendChild(content);
        t.VisualTree = grid;
        return t;
    }

    internal static ControlTemplate PlainTextBoxTemplate()
    {
        var t = new ControlTemplate(typeof(TextBox));
        var host = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
        host.SetValue(FocusableProperty, false);
        host.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        host.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        t.VisualTree = host;
        return t;
    }
}
