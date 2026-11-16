using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace OnRecord;

/// <summary>Small dark dialogs used by Settings (built in code; styles from Themes/DarkDialog.xaml).</summary>
public static class Dialogs
{
    private static readonly Uri StylesUri = new("pack://application:,,,/OnRecord;component/Themes/DarkDialog.xaml");

    /// <summary>A window with the settings look: dark background, dark title bar, shared styles.</summary>
    public static Window MakeDarkWindow(Window? owner, string title, double width)
    {
        var w = new Window
        {
            Title = title, Width = width, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            Owner = owner, ShowInTaskbar = owner == null,
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x22)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xED, 0xED, 0xED)),
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"), FontSize = 13.5,
        };
        w.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = StylesUri });
        w.SourceInitialized += (_, _) => UseDarkTitleBar(w);
        return w;
    }

    public static void UseDarkTitleBar(Window w)
    {
        int on = 1;
        DwmSetWindowAttribute(new WindowInteropHelper(w).Handle, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref on, sizeof(int));
    }

    /// <summary>A warning with two buttons. Returns true only if the user picks <paramref name="confirmText"/>.</summary>
    public static bool Confirm(Window? owner, string title, string heading, string message, string confirmText, string cancelText = "Cancel")
    {
        var w = MakeDarkWindow(owner, title, 440);
        var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        head.Children.Add(new TextBlock
        {
            Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 20,
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xB8, 0x4D)), Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        head.Children.Add(new TextBlock { Text = heading, Style = (Style)w.FindResource("Heading"), Margin = new Thickness(0), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(head);
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xCE)) });

        bool result = false;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        var cancel = new Button { Content = cancelText, Style = (Style)w.FindResource("Btn"), Margin = new Thickness(0, 0, 8, 0), IsCancel = true, IsDefault = true };
        var ok = new Button { Content = confirmText, Style = (Style)w.FindResource("Btn"), Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x3D)), Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)), FontWeight = FontWeights.SemiBold };
        cancel.Click += (_, _) => w.Close();
        ok.Click += (_, _) => { result = true; w.Close(); };
        buttons.Children.Add(cancel); buttons.Children.Add(ok);
        panel.Children.Add(buttons);
        w.Content = panel;
        w.Loaded += (_, _) => cancel.Focus(); // safe default: Enter cancels
        w.ShowDialog();
        return result;
    }

    // ---------------------------------------------------------------- Add mode

    public sealed record NewMode(string Name, string Hex, string Description);

    /// <summary>Asks for a new mode's name, colour and when Claude should use it. Null if cancelled.</summary>
    public static NewMode? AddMode(Window owner, ICollection<string> existingNames)
    {
        var w = MakeDarkWindow(owner, "Add mode", 440);
        var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        TextBlock Label(string t) => new() { Text = t, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 6) };
        TextBlock Hint(string t) => new() { Text = t, Style = (Style)w.FindResource("Hint"), Margin = new Thickness(0, 6, 0, 0) };

        panel.Children.Add(new TextBlock { Text = "New mode", Style = (Style)w.FindResource("Heading") });

        panel.Children.Add(Label("Name"));
        var nameBox = new TextBox { MaxLength = 24 };
        nameBox.SetValue(AutomationIdProperty, "NewModeName");
        panel.Children.Add(nameBox);
        panel.Children.Add(Hint("Lowercase letters, numbers and hyphens, e.g. discord or home-lab."));

        panel.Children.Add(Label("Colour"));
        var colourRow = new DockPanel();
        var swatch = new Border
        {
            Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Margin = new Thickness(0, 0, 10, 0), Cursor = Cursors.Hand,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1), ToolTip = "Pick a colour",
        };
        var hexBox = new TextBox { Text = "#E8508C", FontFamily = new FontFamily("Cascadia Mono, Consolas"), Width = 120, HorizontalAlignment = HorizontalAlignment.Left };
        hexBox.SetValue(AutomationIdProperty, "NewModeHex");
        void Refresh() => swatch.Background = Modes.TryParseHex(hexBox.Text, out var c) ? new SolidColorBrush(c) : Brushes.Transparent;
        hexBox.TextChanged += (_, _) => Refresh();
        swatch.MouseLeftButtonUp += (_, _) =>
        {
            Modes.TryParseHex(hexBox.Text, out var cur);
            if (PickColor(w, cur) is { } picked) hexBox.Text = Modes.ToHex(picked);
        };
        Refresh();
        colourRow.Children.Add(swatch); colourRow.Children.Add(hexBox);
        panel.Children.Add(colourRow);

        panel.Children.Add(Label("When should Claude use it?"));
        var descBox = new TextBox { MaxLength = RulesFile.MaxDescriptionLength };
        descBox.SetValue(AutomationIdProperty, "NewModeDescription");
        panel.Children.Add(descBox);
        panel.Children.Add(Hint("A short description, e.g. \"when helping with Discord bots\". OnRecord adds it to rules.txt for you."));

        var error = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x7B, 0x7B)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0), Visibility = Visibility.Collapsed };
        error.SetValue(AutomationIdProperty, "NewModeError");
        panel.Children.Add(error);

        NewMode? result = null;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        var cancel = new Button { Content = "Cancel", Style = (Style)w.FindResource("Btn"), Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var add = new Button { Content = "Add mode", Style = (Style)w.FindResource("Btn"), IsDefault = true, Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x3D)), Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)), FontWeight = FontWeights.SemiBold };
        add.SetValue(AutomationIdProperty, "NewModeAdd");
        cancel.Click += (_, _) => w.Close();
        add.Click += (_, _) =>
        {
            string name = nameBox.Text.Trim();
            string? problem =
                name.Length == 0 ? "Give the mode a name." :
                !Modes.IsValidNewName(name) ? "Use only lowercase letters, numbers and hyphens (max 24), e.g. discord." :
                existingNames.Contains(name, StringComparer.OrdinalIgnoreCase) ? $"There is already a mode called \"{name}\"." :
                Modes.IsHidden(name) ? $"\"{name}\" is a name OnRecord uses itself. Pick another name." :
                !Modes.TryParseHex(hexBox.Text, out _) ? "The colour must look like #FF8A3D." :
                RulesFile.Sanitize(descBox.Text).Length == 0 ? "Describe when Claude should use this mode." :
                null;
            if (problem != null) { error.Text = problem; error.Visibility = Visibility.Visible; return; }
            Modes.TryParseHex(hexBox.Text, out var col);
            result = new NewMode(name, Modes.ToHex(col), RulesFile.Sanitize(descBox.Text));
            w.Close();
        };
        buttons.Children.Add(cancel); buttons.Children.Add(add);
        panel.Children.Add(buttons);
        w.Content = panel;
        w.Loaded += (_, _) => nameBox.Focus();
        w.ShowDialog();
        return result;
    }

    private static readonly DependencyProperty AutomationIdProperty = System.Windows.Automation.AutomationProperties.AutomationIdProperty;

    // ---------------------------------------------------------------- Windows colour picker

    public static Color? PickColor(Window owner, Color initial)
    {
        var custom = Marshal.AllocCoTaskMem(16 * 4);
        try
        {
            for (int i = 0; i < 16; i++) Marshal.WriteInt32(custom, i * 4, 0xFFFFFF);
            var cc = new CHOOSECOLOR
            {
                lStructSize = Marshal.SizeOf<CHOOSECOLOR>(),
                hwndOwner = new WindowInteropHelper(owner).Handle,
                rgbResult = initial.R | (initial.G << 8) | (initial.B << 16),
                lpCustColors = custom,
                Flags = 0x1 | 0x2, // CC_RGBINIT | CC_FULLOPEN
            };
            if (!ChooseColor(ref cc)) return null;
            return Color.FromRgb((byte)(cc.rgbResult & 0xFF), (byte)((cc.rgbResult >> 8) & 0xFF), (byte)((cc.rgbResult >> 16) & 0xFF));
        }
        finally { Marshal.FreeCoTaskMem(custom); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CHOOSECOLOR
    {
        public int lStructSize;
        public IntPtr hwndOwner, hInstance;
        public int rgbResult;
        public IntPtr lpCustColors;
        public int Flags;
        public IntPtr lCustData, lpfnHook, lpTemplateName;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode)] private static extern bool ChooseColor(ref CHOOSECOLOR cc);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
