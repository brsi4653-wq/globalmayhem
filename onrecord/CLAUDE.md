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
- Main UI is native WPF. WebView2 is ONLY for the side preview panel (artifacts, PDFs, web pages from WEB lines):
  created when something opens there and disposed when it closes. Never a separate browser program.
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
  Plus the hidden built-in `easteregg` #FFC83D (see "Hidden Easter egg mode").
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

## Open Everything: OPEN tag and file cards (1.1.0)
- rules.txt (and the default rules / `RulesFile.FallbackDefault`) tell Claude: "When you find files or folders the
  user wants to see or open, add one line per item, alone: OPEN: <full path>". Existing rules.txt files get that
  line once on start (`RulesFile.AddMissingRules`, tracked by `RulesUpgrade` = 2 in settings.json, so a user who
  deletes it later isn't overridden). `ClaudeCli.LoadRules` warns if it's missing ("file cards may not work").
- `OpenTagHandler` (`Chat/TagHandlers.cs`) hides OPEN lines like the other tags, including while streaming.
  `ChatController.AddFileCard` validates the path (`Chat/FileTargets.Resolve`) and adds a `Chat/FileCard` under
  the reply (one per path per reply; several stack in a left-aligned column of equal width). Paths that are
  refused or don't exist get a small info notice instead of a card.
- Card: thumbnail, name, folder, "size · modified date" (folders: "Folder · modified …"), "Open" / "Open folder"
  and "Show in File Explorer" (`explorer.exe /select,"path"`). Problems (e.g. the file was deleted since) show in
  red on the card, which dims.
- Thumbnails: `Platform/ShellThumbnails.cs` uses IShellItemImageFactory (Windows' own thumbnail system, same as
  File Explorer): THUMBNAILONLY first, then the file-type icon; a Segoe glyph if both fail. One background STA
  thread, one request at a time, 64px x DPI, and only once the card is scrolled into view (`FileCard.CheckOnScreen`).
- Clicking the thumbnail: images (decoded natively, max 2000px on the longest side; Windows' 1024px thumbnail if
  WPF can't decode the format) and text files (first 1 MB, read-only, binary files refused) show natively in the
  side panel (`ShellView.NativePreview`); PDFs show in the same sandboxed WebView2 via a single-file host
  `https://onrecord-file.example/<random>/<name>` answered by `WebResourceRequested` (only that one file, 404 for
  everything else, nothing mapped to a folder). Anything else (videos, Office files…) opens in its default program.
  In file mode the panel's buttons read "Open" and "Show in File Explorer".
- Safety: only fully qualified local drive paths (`C:\...`) that exist; no UNC/network, `\\?\`/`\\.\` device paths,
  alternate data streams or control characters. Nothing is ever opened automatically, only on a click.
  Programs, scripts and shortcuts (`FileTargets.ProgramExtensions`: .exe .bat .cmd .ps1 .vbs .js .msi .lnk .url …)
  ask first ("Run it" / Cancel, Cancel is the default). Existence is re-checked on every click.

## Web pages in the side panel: WEB tag (1.1.1)
- rules.txt (and `RulesFile.WebRule` in `FallbackDefault`) tells Claude to find the exact URL with web search and add
  `WEB: <url>` alone on a line, one per reply. No migration of older rules.txt files (decided by the owner).
- `WebTagHandler` (`Chat/TagHandlers.cs`) hides the line (held back while streaming like the other tags); only the first
  WEB line of a reply acts. `Web/WebTargets.ToUrl`: only absolute http/https with a host opens as a page, anything else
  (text, file:, javascript:, data:, custom protocols) becomes a Google search. Each WEB line also leaves a chip to reopen it.
- `Web/WebPageView.cs`: same panel as artifacts (a web page replaces an artifact/PDF and the reverse). Header in web mode:
  title, Back, Reload, "Open in browser" (current URL, default browser), close. No tabs, no address bar.
- Its OWN environment + persistent profile `%LOCALAPPDATA%\GlobalMayhem\OnRecord\web` (logins survive restarts; never
  shared with the artifact profile `preview-webview`). The environment object lives for the app run; each page gets a new
  controller (old one closed after the new one exists, so its renderer ends). With no page left the browser process
  exits; the kept environment starts a new one on the next page (verified), with a fallback that recreates it.
- New-tab links load in the panel. Sized pop-ups (OAuth sign-in) open in `Web/WebPopupWindow` (owned window, same
  environment, keeps window.opener). External protocol launches are blocked; downloads always go to Downloads.
- Closing the panel, new chat, X / hide to tray, an artifact or a file preview all close the page and its pop-ups.
- Claude can never read or edit the web profile, even with read-anywhere: `ClaudeCli.DeniedPathRules` passes
  `--disallowedTools Read(//c/.../web/**) Edit(//c/.../web/**)` (Claude Code's `//c/...` form; a Read rule also covers
  Glob/Grep; verified with CLI 2.1.325).
- Test switch: `ONRECORD_TRACE_WEB=1` logs page events (host and scheme only) to `logs\web-trace.log`.
- Known risk: Google may refuse account sign-in inside WebView2 ("This browser or app may not be secure"). Don't work around it.

## Open Everything: LAUNCH tag, open installed apps (1.2.1)
- rules.txt (and `RulesFile.LaunchRule` in `FallbackDefault`): when the user asks to open an app, Claude adds
  `LAUNCH: <app name>` alone on a line, using the Start menu name. Never a path. OPEN is for files, WEB for websites.
  No migration of older rules.txt files (decided by the owner).
- `LaunchTagHandler` (`Chat/TagHandlers.cs`) hides the line, held back while streaming like the other tags. Only the first
  LAUNCH line of a reply acts, and only if the user's own typed message asked to open something (`Platform.LaunchIntent`:
  open, launch, start, run, play, load, boot, fire up, bring up, pull up). Otherwise it is hidden and ignored.
- The app list (`Platform/InstalledApps.cs`) is read from `shell:AppsFolder` (the same list as All apps in Start: desktop
  programs, Microsoft Store apps, Steam and other Start shortcuts) on one background STA thread. It is re-read on every
  Alt+Space open (about 0.4 s for ~190 apps), and read on first use otherwise.
- Matching (`Platform/AppMatcher.cs`), case-insensitive, first step that finds anything wins: exact name as written, exact
  name ignoring punctuation, names containing the query as whole words, names the query contains, then near spellings
  (1 letter off, 2 for long names). One app: it opens and the reply gets a small "Opened X" line. Several: a small line
  plus one button per app (up to 8), nothing opens until one is clicked. None: "Couldn't find an installed app called X".
- Launching is exactly a click in Start: `ShellExecuteEx` with the item's shell ID list (SEE_MASK_INVOKEIDLIST) captured
  when the list was read. Nothing Claude writes is ever used as a path, ID or command. Names that look like a path, URL or
  command (\ / : % .exe .lnk) are refused with a Blocked notice.
- Test switches: `ONRECORD_TRACE_LAUNCH=1` logs list size, matches and launches (app names only) to `logs\launch-trace.log`;
  `ONRECORD_TEST_NO_EXTERNAL=1` also stops LAUNCH from opening anything (logs "would launch").
- Expected behaviour (owner approved): a name that is also a website (e.g. "open GeoGuessr") may get a WEB line, because
  the request is ambiguous. Once the user says which ("the GeoGuessr Steam Edition game"), Claude uses LAUNCH. Don't change it.

## Hidden Easter egg mode (1.1.0)
- `Modes.Hidden` holds built-in modes that are always known to the tint tag but never listed, edited, removed or
  reset in Settings (`Modes.Visible` is what Settings shows), never saved in settings.json (a hand-added entry is
  dropped by `AppSettings.RemoveHiddenModes`), and whose names are reserved in Save and "+ Add mode".
  Today that's only `easteregg` (#FFC83D gold); its mode pill reads "OnRecord" (`Modes.DisplayName`).
- Its rules are NOT in rules.txt. They're the `===EASTER EGGS START=== … ===EASTER EGGS END===` block in
  `Claude/BuiltInRules.cs`, appended to the system prompt after rules.txt for every user by `ClaudeCli.LoadRules`.
  "Restore default rules" never touches it. Edit that file to change the eggs (birthday on November 16, Global
  Mayhem mention, "who made you").

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
- OPEN paths are untrusted (see "Open Everything"): validated, never auto-opened, programs confirm first.
- Test switches (env vars, off by default): `ONRECORD_TRACE_VISIBLE`, `ONRECORD_TRACE_NAV`, `ONRECORD_TEST_NO_EXTERNAL`
  (also makes file cards log to `logs\open-trace.log` instead of opening), `ONRECORD_TRACE_THUMBS` (thumbnail results).

## Installer
- `tools\build-installer.ps1` publishes self-contained win-x64 into `publish\win-x64` and compiles
  `installer\OnRecord.iss` (Inno Setup 6, per-user: `winget install JRSoftware.InnoSetup --scope user`)
  into `dist\releases\<Version>_<build date>\OnRecord-Setup-<Version>.exe`, e.g.
  `dist\releases\1.1.0_2026-11-16\OnRecord-Setup-1.1.0.exe`. Version comes from `<Version>` in OnRecord.csproj
  (the About line and the installer read it from there; the .iss default is only a fallback).
- Release names: one per major version, `<Codename>` in OnRecord.csproj (all 1.x = "Applesauce"). Shown as
  "OnRecord 1.2.1 (Applesauce)" in About and the installer's AppVerName. Pick a new name only for a new major version.
  The owner sets the number: a new feature bumps the middle number (1.1.1 web pages, 1.2.1 LAUNCH).
- Installer archive: EVERY installer ever built is kept so any version can be tested later. Never delete, move or
  overwrite one; the script stops if the target exists. 1.0.0 is in `dist\releases\1.0.0_2026-11-15\`.
  (`dist\OnRecord-Setup.exe` is an untouched leftover copy of 1.0.0.) `dist\` is not in git.
- Updating this PC: uninstall the installed copy first with its own uninstaller (`unins000.exe /SILENT` keeps the
  data folder, same as answering No), then run the new installer. The data folder survives both.
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
- Any line `OPEN: <full path>` shows a file card for that file or folder (never opens it by itself).
- A line `WEB: <url>` opens that page in the side panel (first one per reply; see "Web pages in the side panel").
- A line `LAUNCH: <app name>` opens that installed app (first one per reply, only when the user asked to open something).
- The system prompt is rules.txt followed by the app's built-in blocks (`Claude/BuiltInRules.cs`).
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

## Published (public)
- Public home: GitHub repo `brsi4653-wq/globalmayhem` (the globalmayhem.org site repo). OnRecord source is in its `onrecord/` folder
  (with a README and an MIT LICENSE that exist only there). There is no separate OnRecord repo.
- The public copy has its own clean history (author = the GitHub noreply address). Never push THIS repo's history: it holds a private email.
  To publish: export the committed files (`git archive`) into `onrecord/` of the site repo and commit there.
- Never public: `dist\`, `dist\backup`, anything from `%LOCALAPPDATA%`, logs, traces, uncommitted work.
- Releases (tag `onrecord-v<version>`, asset named exactly `OnRecord-Setup.exe`):
  - 1.0.0, 2026-11-16: https://github.com/brsi4653-wq/globalmayhem/releases/tag/onrecord-v1.0.0
  - 1.1.0, 2026-11-16, from commit edb9b5f: https://github.com/brsi4653-wq/globalmayhem/releases/tag/onrecord-v1.1.0
    (SHA-256 4bb7bd384e3a6fc9d8afdff28999513e54aaf887e3cc1f98903f1e64521e8b88). The voice work was not committed then and is not public.
- Website: https://globalmayhem.org is a download page. Its button uses
  `https://github.com/brsi4653-wq/globalmayhem/releases/latest/download/OnRecord-Setup.exe`, and the version, checksum and
  release notes on the page are read from the latest GitHub release, so a new release updates the page by itself.
- The site is on Cloudflare Pages by direct upload (`scripts/deploy.sh` in the site repo). Pushing to GitHub does not publish it.
