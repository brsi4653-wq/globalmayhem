<p align="center"><img src="assets/logo.png" width="120" alt="OnRecord logo"></p>

# OnRecord

A glass Alt+Space popup for Windows that runs your own Claude Code.

Press Alt+Space anywhere, ask Claude, attach files and see what it builds for you. OnRecord is a front end only: it starts the official Claude Code app that is already on your PC and shows the conversation in a small frosted glass window.

OnRecord is made by [Global Mayhem](https://globalmayhem.org).

AI was used in the making of OnRecord.

## Requirements

- Windows 11
- [Claude Code](https://claude.com/claude-code) installed and signed in with a paid Claude plan (Pro or Max)
- Windows transparency effects switched on (Settings, Personalization, Colours). The glass look needs it.

The installer checks both for you and shows the steps if something is missing.

## Install

1. Download [OnRecord-Setup.exe](https://github.com/brsi4653-wq/globalmayhem/releases/latest/download/OnRecord-Setup.exe).
2. Open it. If Windows says "Windows protected your PC", click **More info**, then **Run anyway**. This appears because the installer is not code-signed yet.
3. Follow the installer. It checks your Claude Code setup, then installs OnRecord for your Windows user only. No admin rights are needed.

To check your download, compare its SHA-256 checksum with the one in the release notes:

```powershell
Get-FileHash OnRecord-Setup.exe -Algorithm SHA256
```

## How to use it

- Press **Alt+Space** to open the popup. The shortcut can be changed in Settings.
- Type a message and press Enter. Replies stream in as Claude writes them.
- Attach files with the **+** button, by dragging them onto the popup, or with Ctrl+V (copied files or a screenshot).
- When Claude makes a page or a tool, it opens in a preview panel beside the chat.
- When Claude finds files or folders for you, each one shows as a card with a thumbnail, an Open button and "Show in File Explorer". Nothing opens by itself, only when you click.
- Ask to see a webpage or article and it opens in the side panel, beside the chat.
- Ask to open an app by name and OnRecord opens it, the same as clicking it in Start. If more than one app matches, you pick from buttons.
- **X** hides OnRecord to the tray and ends the chat. The next Alt+Space starts a new one.
- Settings (the gear, or the tray icon) hold the model, the colours, the shortcut and the permissions.

## Default permissions, and why

OnRecord starts Claude Code with a short, fixed list of tools. The aim is that a chat popup can never change or run anything on your PC that you did not expect.

| Action | Default | Why |
| --- | --- | --- |
| Create and edit files inside the OnRecord workspace folder | Allowed | Claude needs somewhere to save what it makes for you |
| Web search and reading web pages | Allowed | Needed to answer questions about current things |
| Read and search files elsewhere on the PC | Off | Your files stay private unless you switch this on in Settings |
| Edit files outside the workspace | Blocked | Nothing outside the workspace can be changed |
| Run commands (Bash, PowerShell) | Blocked | Running commands is the riskiest thing an assistant can do |

Your other Claude Code settings and MCP servers are not loaded into OnRecord. When Claude tries something that is not allowed, the chat shows a clear "Blocked" card.

## Privacy

- OnRecord runs locally. It has no account, no server of its own and no tracking.
- It talks to Claude only by running the Claude Code app on your PC. Your messages go to Anthropic through your own Claude Code sign-in, the same as when you use Claude Code yourself.
- OnRecord never reads, copies or stores your Claude sign-in.
- Settings, the workspace and logs are kept in `%LOCALAPPDATA%\GlobalMayhem\OnRecord`. Uninstalling asks before deleting that folder.

## Build from source

You need Windows 11, the [.NET 10 SDK](https://dotnet.microsoft.com/download) and, for the installer, Inno Setup 6.

Run these from this folder (`onrecord`):

```powershell
dotnet build OnRecord.slnx
dotnet test OnRecord.slnx
dotnet run --project OnRecord
```

To build the installer:

```powershell
winget install JRSoftware.InnoSetup --scope user
powershell -File tools\build-installer.ps1
```

The installer is written to `dist\OnRecord-Setup-<version>.exe`. The version comes from `<Version>` in `OnRecord\OnRecord.csproj`.

## License

MIT. See [LICENSE](LICENSE).

OnRecord is an independent project by Global Mayhem. It is not affiliated with or endorsed by Anthropic.
