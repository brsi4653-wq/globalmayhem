using Microsoft.Win32;

namespace OnRecord.Platform;

/// <summary>Start with Windows via the per-user Run key (no admin needed).</summary>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "OnRecord";
    private const string OldValueName = "Launcher";
    public const string TrayArg = "--tray";

    public static void Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            // Our entry from before the rename. Only if it really is ours (another app may be called "Launcher").
            if (key.GetValue(OldValueName) is string old && old.EndsWith("\\Launcher.exe\" " + TrayArg, StringComparison.OrdinalIgnoreCase))
                key.DeleteValue(OldValueName, throwOnMissingValue: false);
            if (enabled)
                key.SetValue(ValueName, $"\"{AppPaths.ExePath}\" {TrayArg}"); // always points at the running exe
            else
                key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch { /* registry blocked: ignore */ }
    }

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }
}
