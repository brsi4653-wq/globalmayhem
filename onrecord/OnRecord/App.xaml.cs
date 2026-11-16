using System.IO;
using System.Windows;
using OnRecord.Platform;

namespace OnRecord;

public partial class App : Application
{
    private const string InstanceId = "OnRecord.7f3c2a91";
    private const int HotkeyId = 1;

    private Mutex? _singleInstance;
    private EventWaitHandle? _showSignal;
    private RegisteredWaitHandle? _showWait;
    private TrayIcon? _tray;
    private MainWindow? _window;
    private AppSettings _settings = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single instance: a second launch just asks the running one to show the popup.
        _singleInstance = new Mutex(true, $@"Local\{InstanceId}.mutex", out bool isFirst);
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{InstanceId}.show");
        bool startedByWindows = e.Args.Contains(Autostart.TrayArg, StringComparer.OrdinalIgnoreCase);
        if (!isFirst)
        {
            // Launched by hand while already running: bring the popup up. We were started by the user, so we
            // may pass our right to take the foreground on to the running copy.
            // Auto-start (--tray) while already running: do nothing, never pop up.
            if (!startedByWindows)
            {
                AllowSetForegroundWindow(-1 /* ASFW_ANY */);
                _showSignal.Set();
            }
            Shutdown();
            return;
        }

        string? migrationProblem = AppPaths.MigrateFromOldLocation();
        AppPaths.EnsureCreated();
        AppPaths.RemoveOldWebViewData();
        bool firstRun = !File.Exists(AppPaths.Settings);
        _settings = AppSettings.Load();
        if (firstRun)
        {
            // First run after installing: the installer's "Start with Windows" choice (its Run entry) decides.
            _settings.StartWithWindows = Autostart.IsEnabled();
            _settings.Save();
        }
        Modes.Load(_settings.ModeColors);
        string? rulesProblem = null;
        if (_settings.RulesUpgrade < 2)
        {
            // 1.1.0: existing rules.txt files get the OPEN (file card) rule once. Removing it later is respected.
            rulesProblem = RulesFile.AddMissingRules();
            _settings.RulesUpgrade = 2;
            _settings.Save();
        }
        Autostart.Apply(_settings.StartWithWindows);

        _window = new MainWindow(_settings);
        _window.SettingsRequested += OpenSettings;

        _tray = new TrayIcon("OnRecord", Path.Combine(AppContext.BaseDirectory, "Assets", "onrecord-tray.ico"));
        _tray.Clicked += () => _window.ShowPopup();
        _tray.Menu.Add(new TrayIcon.MenuItem("Open OnRecord", () => _window.ShowPopup()));
        _tray.Menu.Add(null);
        _tray.Menu.Add(new TrayIcon.MenuItem("Pin on top", () => _window.SetPinned(!_settings.PinOnTop), () => _settings.PinOnTop));
        _tray.Menu.Add(new TrayIcon.MenuItem("Start with Windows", ToggleAutostart, () => _settings.StartWithWindows));
        _tray.Menu.Add(new TrayIcon.MenuItem("Settings…", OpenSettings));
        _tray.Menu.Add(null);
        _tray.Menu.Add(new TrayIcon.MenuItem("Quit", Quit));

        _tray.HotkeyPressed += id => { if (id == HotkeyId) _window.ShowPopup(); };
        if (!RegisterHotkey(_settings.Hotkey))
        {
            _tray.Notify($"{_settings.Hotkey} is not available",
                $"Another app is using {_settings.Hotkey}, so OnRecord couldn't use it. Open OnRecord from the tray icon, " +
                "then pick another shortcut in Settings (or change it in the other app).",
                warning: true);
        }
        if (migrationProblem != null) _tray.Notify("OnRecord", migrationProblem, warning: true);
        if (rulesProblem != null) _tray.Notify("OnRecord", rulesProblem, warning: true);
        if (AppSettings.LoadProblem != null) _tray.Notify("OnRecord settings", AppSettings.LoadProblem, warning: true);

        _showWait = ThreadPool.RegisterWaitForSingleObject(_showSignal,
            (_, _) => Dispatcher.BeginInvoke(() => _window.ShowPopup()), null, Timeout.Infinite, executeOnlyOnce: false);

        // Started by Windows at login (--tray): stay quietly in the tray. Started by hand: show the popup.
        if (!startedByWindows)
            _window.ShowPopup();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    private SettingsWindow? _settingsWindow;

    private void OpenSettings()
    {
        if (_settingsWindow != null)
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(_settings, () => _window?.RefreshMode(), ChangeHotkey);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        if (_settings.PinOnTop) _settingsWindow.Topmost = true; // don't open behind a pinned popup
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private string? _activeHotkey;

    /// <summary>Registers the shortcut and updates the tray text. False if another app already uses it.</summary>
    private bool RegisterHotkey(string name)
    {
        var choice = Hotkeys.Find(name);
        bool ok = GlobalHotkey.Register(_tray!.Handle, HotkeyId, choice.Modifiers, choice.Key);
        _activeHotkey = ok ? choice.Name : null;
        _tray.SetTooltip(ok ? $"OnRecord ({choice.Name})" : "OnRecord");
        _tray.Menu[0] = new TrayIcon.MenuItem(ok ? $"Open OnRecord\t{choice.Name}" : "Open OnRecord", () => _window!.ShowPopup());
        return ok;
    }

    /// <summary>From Settings: switch to another shortcut. Keeps the old one if the new one is taken.</summary>
    private bool ChangeHotkey(string name)
    {
        if (name == _activeHotkey) return true;
        string? previous = _activeHotkey;
        GlobalHotkey.Unregister(_tray!.Handle, HotkeyId);
        if (RegisterHotkey(name)) return true;
        if (previous != null) RegisterHotkey(previous);
        return false;
    }

    private void ToggleAutostart()
    {
        _settings.StartWithWindows = !_settings.StartWithWindows;
        Autostart.Apply(_settings.StartWithWindows);
        _settings.Save();
    }

    private void Quit()
    {
        _window?.SaveBounds();
        _window?.EndChat();
        if (_window != null) _window.AllowClose = true;
        _settings.Save();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_tray != null)
        {
            GlobalHotkey.Unregister(_tray.Handle, HotkeyId);
            _tray.Dispose();
        }
        _showWait?.Unregister(null);
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
