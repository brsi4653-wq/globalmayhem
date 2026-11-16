using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OnRecord;

/// <summary>
/// Model choice and the modes (colour + when Claude should use each). Colours are saved in settings.json;
/// each mode's description is a line in rules.txt's tint tag list, which Save keeps in sync.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly Action _applied;
    private readonly Func<string, bool> _changeHotkey;
    private string _hotkey;

    /// <param name="applied">Called after saving, so the popup can re-apply colours immediately.</param>
    public SettingsWindow(AppSettings settings, Action applied, Func<string, bool> changeHotkey)
    {
        _settings = settings;
        _applied = applied;
        _changeHotkey = changeHotkey;
        _hotkey = settings.Hotkey;
        InitializeComponent();
        BuildHotkeyChips();
        ModelBox.Text = settings.Model;
        ReadAnywhereBox.IsChecked = settings.AllowReadAnywhere;
        var version = typeof(SettingsWindow).Assembly.GetName().Version;
        AboutText.Text = $"{AppPaths.AppName} {version?.ToString(3)} · by {AppPaths.Publisher}";
        try
        {
            var logo = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "logo-64.png");
            if (System.IO.File.Exists(logo)) AboutLogo.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(logo));
            else AboutLogo.Visibility = Visibility.Collapsed;
        }
        catch { AboutLogo.Visibility = Visibility.Collapsed; }
        LoadRows(CurrentModes());
        SourceInitialized += (_, _) => Dialogs.UseDarkTitleBar(this);
    }

    private sealed record ModeEntry(string Name, string Hex, string Description);

    /// <summary>The saved modes: colours from settings, descriptions from rules.txt (or the defaults).</summary>
    private static List<ModeEntry> CurrentModes()
    {
        var fromRules = RulesFile.ParseTints(RulesFile.ReadUser() ?? "")
            .GroupBy(t => t.Name).ToDictionary(g => g.Key, g => g.First().Description, StringComparer.OrdinalIgnoreCase);
        var defaults = RulesFile.DefaultDescriptions();
        return Modes.Colors.Select(kv => new ModeEntry(
            kv.Key, Modes.ToHex(kv.Value),
            fromRules.TryGetValue(kv.Key, out var d) ? d : defaults.GetValueOrDefault(kv.Key, ""))).ToList();
    }

    private static List<ModeEntry> DefaultModes()
    {
        var defaults = RulesFile.DefaultDescriptions();
        return Modes.Defaults.Select(kv => new ModeEntry(kv.Key, kv.Value, defaults.GetValueOrDefault(kv.Key, ""))).ToList();
    }

    // ---------------------------------------------------------------- mode rows

    private sealed class Row
    {
        public required Grid Root;
        public required TextBox Name;
        public required TextBox Hex;
        public required TextBox Description;
    }

    private readonly List<Row> _rows = new();

    private void LoadRows(IEnumerable<ModeEntry> modes)
    {
        ModeRows.Children.Clear();
        _rows.Clear();
        foreach (var m in modes) AddRow(m);
    }

    private void AddRow(ModeEntry mode)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition());

        var swatch = new Border
        {
            Width = 26, Height = 26, CornerRadius = new CornerRadius(13),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Left, Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = "Pick a colour",
        };
        var nameBox = new TextBox { Text = mode.Name, Margin = new Thickness(0, 0, 8, 0), MaxLength = 24 };
        var hexBox = new TextBox { Text = mode.Hex, FontFamily = new FontFamily("Cascadia Mono, Consolas") };
        var descBox = new TextBox
        {
            Text = mode.Description, Margin = new Thickness(0, 6, 0, 0), FontSize = 12.5, MaxLength = RulesFile.MaxDescriptionLength,
            Foreground = new SolidColorBrush(Color.FromRgb(0xB8, 0xB8, 0xC0)),
            ToolTip = "When Claude should use this mode (saved in rules.txt)",
        };
        var remove = new Button
        {
            Content = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 11,
            Style = (Style)FindResource("Chip"), Margin = new Thickness(8, 0, 0, 0), ToolTip = "Remove mode",
        };
        Grid.SetColumn(nameBox, 1); Grid.SetColumn(hexBox, 2); Grid.SetColumn(remove, 3);
        Grid.SetRow(descBox, 1); Grid.SetColumn(descBox, 1); Grid.SetColumnSpan(descBox, 2);
        grid.Children.Add(swatch); grid.Children.Add(nameBox); grid.Children.Add(hexBox); grid.Children.Add(remove); grid.Children.Add(descBox);

        var row = new Row { Root = grid, Name = nameBox, Hex = hexBox, Description = descBox };
        void Refresh() => swatch.Background = Modes.TryParseHex(hexBox.Text, out var c) ? new SolidColorBrush(c) : Brushes.Transparent;
        hexBox.TextChanged += (_, _) => Refresh();
        swatch.MouseLeftButtonUp += (_, _) =>
        {
            Modes.TryParseHex(hexBox.Text, out var current);
            if (Dialogs.PickColor(this, current) is { } picked) hexBox.Text = Modes.ToHex(picked);
        };
        remove.Click += (_, _) =>
        {
            if (string.Equals(nameBox.Text.Trim(), Modes.Default, StringComparison.OrdinalIgnoreCase))
            { ShowError("The \"claude\" mode is the default and must stay."); return; }
            ModeRows.Children.Remove(grid); _rows.Remove(row);
        };
        Refresh();

        ModeRows.Children.Add(grid);
        _rows.Add(row);
    }

    private void AddMode_Click(object sender, RoutedEventArgs e)
    {
        var names = _rows.Select(r => r.Name.Text.Trim()).Where(n => n.Length > 0).ToList();
        if (Dialogs.AddMode(this, names) is { } m)
        {
            AddRow(new ModeEntry(m.Name, m.Hex, m.Description));
            ShowInfo($"Added \"{m.Name}\". Click Save to apply it (from the next new chat).");
        }
    }

    private void ResetColors_Click(object sender, RoutedEventArgs e) => LoadRows(DefaultModes());

    private void ModelPreset_Click(object sender, RoutedEventArgs e) => ModelBox.Text = (string)((Button)sender).Tag;

    private void OpenRules_Click(object sender, RoutedEventArgs e)
    {
        bool go = Dialogs.Confirm(this, "Edit rules.txt", "Edit rules.txt carefully",
            "rules.txt controls how Claude talks to OnRecord. Editing it can break the app: tints, artifacts or replies " +
            "may stop working. Only change it carefully.\n\nTo add or change modes, use the mode list in Settings instead: " +
            "OnRecord updates rules.txt for you. You can always use \"Restore default rules\" to undo your edits.",
            "Open anyway");
        if (!go) return;
        RulesFile.EnsureUserCopy();
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = System.IO.Path.Combine(Environment.SystemDirectory, "notepad.exe"), // full path: no search-path planting
            ArgumentList = { AppPaths.Rules },
            UseShellExecute = false,
        });
    }

    private void RestoreRules_Click(object sender, RoutedEventArgs e)
    {
        bool go = Dialogs.Confirm(this, "Restore default rules", "Restore the default rules.txt?",
            "This replaces rules.txt with the original version. Any edits you made to it by hand are lost. " +
            "Your saved modes (including ones you added) are kept in the tint list.",
            "Restore");
        if (!go) return;
        var saved = CurrentModes().Select(m => new RulesFile.Tint(m.Name, m.Description));
        var error = RulesFile.RestoreDefault(saved);
        if (error != null) ShowError(error); else ShowInfo("Default rules restored. They apply from the next new chat.");
    }

    // ---------------------------------------------------------------- save

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var entries = new List<ModeEntry>();
        foreach (var row in _rows)
        {
            string name = row.Name.Text.Trim();
            if (name.Length == 0) continue;
            if (!Modes.IsValidNewName(name)) { ShowError($"\"{name}\" isn't a valid mode name. Use lowercase letters, numbers and hyphens (max 24)."); return; }
            if (!Modes.TryParseHex(row.Hex.Text, out var c)) { ShowError($"The colour for \"{name}\" must look like #FF8A3D."); return; }
            if (entries.Any(x => x.Name == name)) { ShowError($"The mode \"{name}\" is listed twice."); return; }
            entries.Add(new ModeEntry(name, Modes.ToHex(c), RulesFile.Sanitize(row.Description.Text)));
        }
        if (!entries.Any(x => x.Name == Modes.Default)) { ShowError("The \"claude\" mode is the default and must stay."); return; }

        string model = ModelBox.Text.Trim();
        if (model.Length > 0 && !Claude.ClaudeCli.IsSafeModel(model)) { ShowError("The model name must start with a letter or number and can only contain letters, numbers, dots, hyphens, underscores, colons, [ ] and /."); return; }

        if (_hotkey != _settings.Hotkey)
        {
            if (!_changeHotkey(_hotkey))
            {
                ShowError($"{_hotkey} is already used by another app. Pick a different shortcut, or free it in that app first.");
                return;
            }
            _settings.Hotkey = _hotkey;
        }

        var map = entries.ToDictionary(x => x.Name, x => x.Hex);
        _settings.Model = model;
        _settings.AllowReadAnywhere = ReadAnywhereBox.IsChecked == true;
        bool isDefaultMap = map.Count == Modes.Defaults.Count
                            && Modes.Defaults.All(d => map.TryGetValue(d.Key, out var v) && string.Equals(v, d.Value, StringComparison.OrdinalIgnoreCase));
        _settings.ModeColors = isDefaultMap ? new() : map;
        _settings.Save();
        Modes.Load(_settings.ModeColors);

        // Keep Claude's tint list in rules.txt in step with the modes (applies from the next new chat).
        var rulesError = RulesFile.SyncTintList(entries.Select(x => new RulesFile.Tint(x.Name, x.Description)));
        _applied();
        if (rulesError != null) { ShowError("Colours saved. " + rulesError); return; }
        Close();
    }


    private void BuildHotkeyChips()
    {
        HotkeyChips.Children.Clear();
        foreach (var choice in Platform.Hotkeys.Choices)
        {
            bool selected = choice.Name == _hotkey;
            var chip = new Button
            {
                Content = choice.Name, Style = (Style)FindResource("Chip"), Margin = new Thickness(0, 0, 6, 6),
                Background = selected ? new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x3D)) : new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
                Foreground = selected ? new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)) : new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2)),
                FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal,
            };
            chip.Click += (_, _) => { _hotkey = choice.Name; BuildHotkeyChips(); };
            HotkeyChips.Children.Add(chip);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void ShowError(string text)
    {
        ErrorText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x7B, 0x7B));
        ErrorText.Text = text;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void ShowInfo(string text)
    {
        ErrorText.Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0xD8, 0x9A));
        ErrorText.Text = text;
        ErrorText.Visibility = Visibility.Visible;
    }
}
