# OnRecord

A Spotlight/Siri-style popup for Windows that is a front end for Claude, driven
by the official Claude Code CLI (`claude`). Published by Global Mayhem.
Later it will gain more modes and tools (system search, web search, Discord bots).

## Where things live
- Project root (all code, git repo, this file): the repository root (the folder containing this file).
  Never put project files anywhere else. Run all build commands from here.
- Runtime data: `%LOCALAPPDATA%\GlobalMayhem\OnRecord\`
  - `workspace\` is the working directory of the `claude` process (NOT this code folder)
  - `workspace\artifacts\` is where Claude saves HTML artifacts
  - `rules.txt` is copied here from the app on first run; the user edits this copy
  - settings (window size/position, pin, model, colours)

## Fixed tech decisions (do not change without asking the project owner)
- C# + WPF on .NET 10 (current LTS). No Rust, Electron, Tauri. Keep RAM as low as possible.
- Main UI is native WPF. WebView2 is ONLY for the artifact preview panel: it is
  created when an artifact opens and disposed when it closes.
- Chat markdown is rendered natively (Markdig + our own small WPF renderer), not in a web view.
- Talk to Claude ONLY by running the `claude` CLI as a child process. Never read,
  copy or reuse the login token. Never call the Anthropic API directly.

## Claude process / sessions
- On each Alt+Space open after being closed, start ONE persistent process:
  `claude -p --input-format stream-json --output-format stream-json --verbose
   --include-partial-messages --append-system-prompt <rules> ...`
  Each user message is written to stdin as one JSON line:
  `{"type":"user","message":{"role":"user","content":"..."}}`
  All messages while open go to that same process/session (tested in stage 0: context is kept).
- Clicking X ends the process. The next open starts a brand new session. "New chat" button does the same.
- If a message is sent while a reply is streaming, queue it silently and send it when the `result` event arrives.
- rules.txt goes through `--append-system-prompt` (never as a chat message), after replacing
  `{ARTIFACTS_DIR}` with the full artifacts path.
- If the claude process exits or crashes unexpectedly, show a clear message in the chat and
  start a fresh process automatically on the next message.
- Permission flags (`Claude/ClaudeCli.cs`, verified in stage 3): `--permission-mode acceptEdits`
  (edits auto-approved only inside the working dir = workspace), `--tools` whitelist
  (Read, Glob, Grep, Edit, Write, NotebookEdit, WebSearch, WebFetch, ToolSearch: no Bash/PowerShell,
  because Claude Code auto-approves "harmless" shell commands like `echo` even when not allowed),
  `--allowedTools` WebSearch/WebFetch (+ Read/Glob/Grep only with the read-anywhere opt-in), `--setting-sources ""` and `--strict-mcp-config`
  so the user's other Claude Code settings and MCP servers don't leak in.
- Denials arrive as `{"type":"system","subtype":"permission_denied","message":...}` and are shown as an
  amber "Blocked" card. Crashes are logged to `%LOCALAPPDATA%\GlobalMayhem\OnRecord\logs\claude-crash.log`.
- All claude processes are in a kill-on-close job object, so they die with OnRecord.
- Always connect stdin. Without it, `claude -p` waits 3s for stdin.
- Permissions: auto-allow file create/edit inside the workspace and web search/fetch. Read/search
  (Read, Glob, Grep) anywhere on the PC is an OPT-IN setting (`AllowReadAnywhere`, off by default;
  off = reads only inside the workspace). No editing outside the workspace. Running commands
  (Bash etc.) stays blocked. Denied actions must be shown clearly in chat.

## Look and feel
- Dark, ~30px rounded corners, frosted acrylic glass that stays frosted when unfocused,
  smooth edges, soft shadow, 1px subtle light border, no lag while dragging.
- Tint gradient over the glass: ~32% tint opacity at top fading to ~8% at bottom, over a dark base.
- Mode colours live in ONE map (later editable in settings):
  claude #FF8A3D (default), code #9B6BFF, search #3D8BFF, web #2EC4A6.
  Mode change animates the tint over ~500ms.
- Top bar: round X (left), mode pill with coloured dot + name (centre), round expand (right).
  No Windows title bar; drag from the top bar. Resizable from all edges, grip bottom-right.
- Chat: user messages as right-aligned translucent white pills; Claude as plain text on the left.
- Bottom: pill input bar with round + (left), text box, round send filled with tint (right); room for a mic later.
- When wide and an artifact is open: chat left (~420px), artifact preview right in a rounded
  light panel with the file name, "Open in browser" and "Show file".

## How the glass works (stage 1, keep this structure)
- Top-level `MainWindow`: WindowStyle=None, ResizeMode=NoResize, NOT AllowsTransparency (layered
  windows are slow). DWM frame extended over the whole client + transparent WPF background gives
  per-pixel alpha. DWM corners, border, system backdrop all switched off (`Glass/GlassBackdrop.cs`).
- Behind the UI: a Windows.UI.Composition tree on the top-level HWND (DesktopWindowTarget):
  DropShadow LayerVisual + SpriteVisual with `Compositor.CreateHostBackdropBrush()` (real acrylic),
  both clipped by an antialiased 30px rounded-rectangle geometry. Needs DWMWA_USE_HOSTBACKDROPBRUSH.
  Stays frosted when unfocused, moves with the window on the GPU (no drag lag).
- Windows always draws that composition tree ABOVE the top-level window's own surface, so the WPF UI
  (`ShellView`) lives in a per-pixel-transparent CHILD window (`Glass/TransparentChildHost.cs`).
  Layered child windows need the Windows 10 supportedOS entry in `app.manifest`.
- Resizing: no WS_THICKFRAME (it brings back system border/shadow), so `Glass/GlassChrome.cs` does
  hit-testing and starts the resize loop with WM_SYSCOMMAND SC_SIZE. The child returns HTTRANSPARENT
  on edges so the parent gets them. A 24px transparent margin around the glass holds the shadow
  and is part of the resize zone.
- Tint/dark base/border are WPF layers in `ShellView.xaml`. Mode colours: `Modes.cs` only.
- Glass requires Windows "Transparency effects" to be on (Settings > Personalization > Colours).

## Artifact preview (stage 4)
- `Artifacts/ArtifactWebView.cs`: raw CoreWebView2Controller parented to the TOP-LEVEL window (WebView2
  can't show inside the layered UI child). Kept over `WebSlot` in `ShellView` via `WebSlotChanged`;
  the UI child is pushed to HWND_BOTTOM so the page is on top. Closed (processes released) when the
  panel closes, on new chat and on X. Profile data: `%LOCALAPPDATA%\GlobalMayhem\OnRecord\webview`.
- Split when glass width >= 900: chat 420px + preview. Narrower: preview covers the chat. If the window
  is narrow when an artifact opens, it is widened (within the screen) and narrowed back on close
  unless the user resized it meanwhile.
- Each ARTIFACT also leaves a chip in the reply so a closed preview can be reopened.

## Settings (stage 5)
- `SettingsWindow` (tray "Settings…" or the gear in the popup). Saves `Model` and `ModeColors` in
  settings.json. Colours apply live (`Modes.Load` + `MainWindow.RefreshMode`); the model applies from
  the next new session (passed as `--model`, empty = CLI default). Defaults live in `Modes.Defaults`.
- Mode descriptions live in rules.txt's tint tag list (`RulesFile.cs`). Settings reads them from there and
  Save rewrites ONLY that contiguous `[tint:name] description` block (sanitised: one line, no tags/brackets).
  "+ Add mode" is a form (name, colour, when to use it). "Open rules.txt" warns first; "Restore default
  rules" puts back the shipped file with the current modes in the tint list. Dialog styles: `Themes/DarkDialog.xaml`
  (merged per window, never app-wide, so the popup's own styles stay untouched).

## Attachments
- + button (file picker, multi-select), drag and drop onto the popup, Ctrl+V (copied files, or an image such as a
  screenshot, saved as PNG). Chips above the input bar (image preview, x to remove). Max 30 MB per file.
- On send (`Chat/Attachments.cs`): copied into `workspace\attachments` with FileMode.CreateNew (duplicates get
  " (2)"; never overwrite, originals only read). Claude gets the text plus "Attached files:" and the full paths and
  reads them with its Read tool (images and PDFs work). Attachments-only messages are allowed.
- Unsent attachments are cleared by new chat and by closing the popup.

## Shortcut
- Configurable in Settings (`Platform/Hotkeys.cs` presets, default Alt+Space). Applied immediately; a shortcut
  another app has registered is refused and the current one kept. Tray tooltip/menu show the active one.

## Security rules (stage 6 review; keep these)
- `claude` is started with `ProcessStartInfo.ArgumentList`, never a command string, and never via a .cmd
  (an npm `claude.cmd` is resolved to `node.exe cli.js`). User-editable values go as `--flag=value`
  (`--append-system-prompt=`, `--model=`), so they can't become extra flags. Model names are whitelisted.
- Never read the login token. stderr/errors go through `ClaudeCli.Redact` before being logged or shown.
- ARTIFACT names: `Artifacts/ArtifactName.cs` accepts only a plain `*.html` name directly in the artifacts
  folder (no folders, `..`, drives, UNC, streams, device names, reparse points). Refused, never fixed up.
- Preview pages are untrusted: virtual host `https://onrecord-artifacts.example/` mapped to the artifacts
  folder only (never file://), own profile `preview-webview`, no host objects/web messages/dev tools/
  downloads/permissions/external protocols, main frame can't navigate away; user-clicked links open in the
  default browser for http/https/mailto only.
- Settings and rules.txt: damaged or hand-edited files never crash the app; defaults are used and the user
  is told (tray notification / chat notice). A damaged settings.json is kept as `settings.json.damaged-*`.
- Programs the app starts itself use full paths (notepad, explorer).
- Test switches (env vars, off by default): `ONRECORD_TRACE_VISIBLE`, `ONRECORD_TRACE_NAV`, `ONRECORD_TEST_NO_EXTERNAL`.

## Installer
- `tools\build-installer.ps1` publishes self-contained win-x64 into `publish\win-x64` and compiles
  `installer\OnRecord.iss` (Inno Setup 6, per-user: `winget install JRSoftware.InnoSetup --scope user`)
  into `dist\OnRecord-Setup-<Version>.exe`. Version comes from `<Version>` in OnRecord.csproj.
- Per-user, no admin: app in `%LOCALAPPDATA%\Programs\GlobalMayhem\OnRecord`, Start menu shortcut,
  optional "start with Windows" (HKCU Run, same value the app manages). On first run the app adopts that choice.
- Wizard: Welcome (logo image from `tools\make-wizard-image.ps1`) -> Requirements page (Claude Code found
  in `%USERPROFILE%\.local\bin`/PATH/npm, on the registry PATH, signed in via `claude auth status` (paid plan;
  the email is never shown), WebView2, transparency; steps + Copy + "Check again" when not ready; continuing
  anyway asks first) -> Options page (start with Windows, ticked; Alt+Space note with a live RegisterHotKey
  check, OnRecord's own mutex is not a conflict; desktop shortcut, unticked) -> Ready -> Finish (launch).
  Test build of the not-ready branch: `ISCC /DTestNotReady` (never ship it).
- Install/uninstall end running copies first (taskkill: the tray app has no window to close politely).
  Uninstall removes the app, shortcut and Run entry, and ASKS before deleting the data folder (never in silent mode).

## Known gaps / follow-ups
- Accessibility: UI Automation sees nothing inside the transparent child window (`TransparentChildHost`),
  so screen readers can't read the popup. Fix later with an automation peer for the hosted HwndSource.

## Behaviour
- Tray app, starts with Windows (HKCU Run key), per-user installer.
- Launch behaviour: launched by hand (exe, shortcut) = popup opens straight away on screen. Auto-start
  with Windows passes `--tray` = starts quietly in the tray, never pops up. A second launch by hand
  while running brings up the running popup; a second `--tray` launch does nothing.
- Global hotkey Alt+Space shows and focuses the window (it may be taken on some PCs). Show a tray notification if registering fails.
- Does NOT close on focus loss. X hides to the tray; Quit is in the tray menu. Optional pin-on-top.
- Remembers size and position.

## Reply tags (hidden from the chat)
- Line 1 `[tint:<mode>]` switches mode/colour (animated). Unknown/missing: keep the current mode.
- Any line `ARTIFACT: file.html` opens that file from the artifacts folder in the preview panel.
- Implemented as a list of tag handlers so new tags can be added, each with its own action.

## Build order (stop after each stage, run it, the owner tests, then commit)
0. Environment + repo + this file (done)
1. Window shell (glass, corners, top bar, drag, resize)
2. Tray, autostart, hotkey, hide to tray, remember bounds, pin on top
3. Chat UI + claude connection
4. Tag handling, artifacts, WebView2 preview, split layout
5. Settings (model, colour map)
End: Windows installer

## Environment (verified in stage 0)
- .NET SDK 10.0.404, Git 2.55, Claude Code 2.1.325, WebView2 runtime 157

## Working rules
- Build and run the app after every change, and fix errors before reporting a step as done.
