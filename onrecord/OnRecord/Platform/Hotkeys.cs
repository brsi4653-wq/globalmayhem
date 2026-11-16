namespace OnRecord.Platform;

/// <summary>The keyboard shortcuts OnRecord can open with (chosen in Settings; Alt+Space by default).</summary>
public static class Hotkeys
{
    public sealed record Choice(string Name, uint Modifiers, uint Key);

    public const string Default = "Alt+Space";

    public static readonly Choice[] Choices =
    [
        new("Alt+Space", GlobalHotkey.MOD_ALT, GlobalHotkey.VK_SPACE),
        new("Ctrl+Space", GlobalHotkey.MOD_CONTROL, GlobalHotkey.VK_SPACE),
        new("Ctrl+Alt+Space", GlobalHotkey.MOD_CONTROL | GlobalHotkey.MOD_ALT, GlobalHotkey.VK_SPACE),
        new("Ctrl+Shift+Space", GlobalHotkey.MOD_CONTROL | GlobalHotkey.MOD_SHIFT, GlobalHotkey.VK_SPACE),
        new("Alt+Shift+Space", GlobalHotkey.MOD_ALT | GlobalHotkey.MOD_SHIFT, GlobalHotkey.VK_SPACE),
    ];

    public static bool IsKnown(string? name) => Choices.Any(c => c.Name == name);

    public static Choice Find(string? name) => Choices.FirstOrDefault(c => c.Name == name) ?? Choices[0];
}
