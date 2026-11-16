using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace OnRecord.Platform;

/// <summary>
/// System tray icon using Shell_NotifyIcon directly (no WinForms, keeps RAM down).
/// Owns a hidden message-only window that also receives the global hotkey.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    public sealed record MenuItem(string Text, Action OnClick, Func<bool>? IsChecked = null);

    private const int WM_APP_TRAY = 0x8000 + 1;
    private const int NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    private const int NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4, NIF_INFO = 0x10, NIF_SHOWTIP = 0x80;
    private const int NIIF_WARNING = 2, NIIF_INFO = 1;
    private const int NOTIFYICON_VERSION_4 = 4;
    private const int NIN_SELECT = 0x400, NIN_KEYSELECT = 0x401, WM_CONTEXTMENU = 0x7B;
    private const int WM_HOTKEY = 0x0312;
    private const uint MF_STRING = 0, MF_CHECKED = 8, MF_SEPARATOR = 0x800;
    private const uint TPM_RETURNCMD = 0x100, TPM_RIGHTBUTTON = 2, TPM_BOTTOMALIGN = 0x20;

    private readonly HwndSource _window;
    private readonly IntPtr _icon;
    private readonly int _taskbarCreatedMsg;
    private string _tooltip;
    private bool _added;

    public event Action? Clicked;
    public event Action<int>? HotkeyPressed;
    public List<MenuItem?> Menu { get; } = new(); // null = separator

    public IntPtr Handle => _window.Handle;

    public TrayIcon(string tooltip, string iconPath)
    {
        _tooltip = tooltip;
        // Message-only window (parent = HWND_MESSAGE): invisible, never in alt-tab.
        _window = new HwndSource(new HwndSourceParameters("OnRecordTray") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0, WindowStyle = 0 });
        _window.AddHook(WndProc);
        _taskbarCreatedMsg = RegisterWindowMessage("TaskbarCreated");
        _icon = File.Exists(iconPath)
            ? LoadImage(IntPtr.Zero, iconPath, 1 /*IMAGE_ICON*/, GetSystemMetrics(49) /*SM_CXSMICON*/, GetSystemMetrics(50), 0x10 /*LR_LOADFROMFILE*/)
            : LoadIcon(IntPtr.Zero, (IntPtr)32512);
        Add();
    }

    private void Add()
    {
        var data = NewData(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        _added = Shell_NotifyIcon(NIM_ADD, ref data);
        data.uVersion = NOTIFYICON_VERSION_4;
        Shell_NotifyIcon(NIM_SETVERSION, ref data);
    }

    public void SetTooltip(string tooltip)
    {
        _tooltip = tooltip;
        var data = NewData(NIF_TIP | NIF_SHOWTIP);
        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    /// <summary>Shows a Windows notification from the tray icon.</summary>
    public void Notify(string title, string text, bool warning = false)
    {
        var data = NewData(NIF_INFO);
        data.szInfoTitle = title;
        data.szInfo = text;
        data.dwInfoFlags = warning ? NIIF_WARNING : NIIF_INFO;
        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    private NOTIFYICONDATAW NewData(int flags) => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = _window.Handle,
        uID = 1,
        uFlags = flags,
        uCallbackMessage = WM_APP_TRAY,
        hIcon = _icon,
        szTip = _tooltip,
        szInfo = "",
        szInfoTitle = "",
    };

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_APP_TRAY)
        {
            int evt = (int)(lParam.ToInt64() & 0xFFFF);
            if (evt == NIN_SELECT || evt == NIN_KEYSELECT) Clicked?.Invoke();
            else if (evt == WM_CONTEXTMENU) ShowMenu();
            handled = true;
        }
        else if (msg == WM_HOTKEY)
        {
            HotkeyPressed?.Invoke(wParam.ToInt32());
            handled = true;
        }
        else if (msg == _taskbarCreatedMsg)
        {
            Add(); // Explorer restarted: put the icon back
        }
        return IntPtr.Zero;
    }

    private void ShowMenu()
    {
        IntPtr menu = CreatePopupMenu();
        for (int i = 0; i < Menu.Count; i++)
        {
            var item = Menu[i];
            if (item == null) AppendMenu(menu, MF_SEPARATOR, 0, null);
            else AppendMenu(menu, MF_STRING | (item.IsChecked?.Invoke() == true ? MF_CHECKED : 0), (UIntPtr)(i + 1), item.Text);
        }
        GetCursorPos(out var pt);
        SetForegroundWindow(_window.Handle); // required so the menu closes when clicking elsewhere
        int cmd = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_BOTTOMALIGN, pt.X, pt.Y, _window.Handle, IntPtr.Zero);
        DestroyMenu(menu);
        if (cmd > 0 && Menu[cmd - 1] is MenuItem chosen) chosen.OnClick();
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = NewData(0);
            Shell_NotifyIcon(NIM_DELETE, ref data);
            _added = false;
        }
        _window.Dispose();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public int uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(int msg, ref NOTIFYICONDATAW data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int RegisterWindowMessage(string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadImage(IntPtr inst, string name, int type, int cx, int cy, int flags);
    [DllImport("user32.dll")] private static extern IntPtr LoadIcon(IntPtr inst, IntPtr name);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, string? text);
    [DllImport("user32.dll")] private static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr tpm);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT pt);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
}
