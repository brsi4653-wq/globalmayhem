using System.Runtime.InteropServices;

namespace OnRecord.Platform;

/// <summary>System-wide hotkey via RegisterHotKey. WM_HOTKEY arrives at the owner window (the tray window).</summary>
public static class GlobalHotkey
{
    public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;
    public const uint VK_SPACE = 0x20;

    public static bool Register(IntPtr hwnd, int id, uint modifiers, uint key) =>
        RegisterHotKey(hwnd, id, modifiers | MOD_NOREPEAT, key);

    public static void Unregister(IntPtr hwnd, int id) => UnregisterHotKey(hwnd, id);

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
