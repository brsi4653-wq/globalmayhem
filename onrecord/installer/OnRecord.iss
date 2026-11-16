; OnRecord per-user installer (no admin rights needed). Published by Global Mayhem.
; Build with tools\build-installer.ps1 (publishes the app self-contained first, then compiles this with Inno Setup 6).
; Screens: Welcome -> Requirements (Claude Code, WebView2, transparency) -> Options -> Ready/Install -> Finish.

#define AppName "OnRecord"
#define AppPublisher "Global Mayhem"
#define AppExe "OnRecord.exe"
#define AppMutex "Local\OnRecord.7f3c2a91.mutex"
#ifndef AppVersion
  #define AppVersion "1.2.1"
#endif
#ifndef AppCodename
  #define AppCodename "Applesauce"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish\win-x64"
#endif

[Setup]
AppId={{6E0C6B1E-4F7A-4E0B-9C1D-0B5E8A3F2D71}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion} ({#AppCodename})
AppPublisher={#AppPublisher}
AppCopyright=© {#AppPublisher}
VersionInfoCompany={#AppPublisher}
VersionInfoProductName={#AppName}
VersionInfoVersion={#AppVersion}
; Per-user: %LOCALAPPDATA%\Programs\GlobalMayhem\OnRecord, shortcuts in the user's own Start menu. No admin.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=
DefaultDirName={autopf}\GlobalMayhem\OnRecord
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableWelcomePage=no
DisableReadyPage=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
WizardStyle=modern
WizardSizePercent=120
SetupIconFile=..\OnRecord\Assets\onrecord.ico
WizardSmallImageFile=logo-55.png,logo-110.png
WizardImageFile=wizard-164.png,wizard-410.png
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
; tools\build-installer.ps1 overrides this with /O dist\releases\<version>_<build date> (old installers are kept)
OutputDir=..\dist\releases
OutputBaseFilename=OnRecord-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
CloseApplications=force
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
WelcomeLabel1=Welcome to OnRecord
WelcomeLabel2=OnRecord is a popup chat for Windows: press Alt+Space anywhere to ask Claude, attach files and see what it builds for you.%n%nIt talks to Claude through the official Claude Code app on this PC, so you'll need Claude Code and a paid Claude plan (Pro or Max). The next screen checks that for you.
FinishedHeadingLabel=OnRecord is installed
FinishedLabelNoIcons=OnRecord is ready.%n%nPress Alt+Space anytime to open OnRecord.
FinishedLabel=OnRecord is ready.%n%nPress Alt+Space anytime to open OnRecord.

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Comment: "Chat with Claude in a popup (Alt+Space)"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Check: WantDesktopIcon

[Registry]
; Same Run entry the app itself manages (Settings / tray menu "Start with Windows").
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#AppName}"; \
    ValueData: """{app}\{#AppExe}"" --tray"; Check: WantAutostart

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch OnRecord now"; Flags: nowait postinstall skipifsilent

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  InstallCommand = 'irm https://claude.ai/install.ps1 | iex';
  PathCommand = '[Environment]::SetEnvironmentVariable(''Path'', [Environment]::GetEnvironmentVariable(''Path'',''User'') + '';'' + $env:USERPROFILE + ''\.local\bin'', ''User'')';
  WebView2Guid = '{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  MOD_ALT = 1; MOD_NOREPEAT = $4000; VK_SPACE = $20;
  ColorOk = $00308A30; ColorBad = $002020C8; ColorWarn = $000A7FD0;

function RegisterHotKey(hWnd: HWND; id: Integer; fsModifiers, vk: Cardinal): BOOL;
  external 'RegisterHotKey@user32.dll stdcall';
function UnregisterHotKey(hWnd: HWND; id: Integer): BOOL;
  external 'UnregisterHotKey@user32.dll stdcall';

var
  ReqPage, OptPage: TWizardPage;
  RowIcon, RowText: array[0..4] of TNewStaticText;
  SummaryLabel, StepsLabel, CmdLabel: TNewStaticText;
  CmdEdit: TNewEdit;
  CopyButton, CheckButton: TNewButton;
  AutostartBox, DesktopBox: TNewCheckBox;
  AutostartHint, HotkeyNote, HotkeyStatus: TNewStaticText;
  ClaudePath: String;
  ClaudeInstalled, ClaudeOnPath, ClaudeSignedIn, ClaudePaid, ClaudeReady: Boolean;
  PlanName: String;

{ ---------------------------------------------------------------- helpers }

function ExpandEnvVars(S: String): String;
var
  P, Q: Integer;
  Name: String;
begin
  Result := '';
  while True do
  begin
    P := Pos('%', S);
    if P = 0 then Break;
    Q := Pos('%', Copy(S, P + 1, Length(S)));
    if Q = 0 then Break;
    Name := Copy(S, P + 1, Q - 1);
    Result := Result + Copy(S, 1, P - 1) + GetEnv(Name);
    S := Copy(S, P + Q + 1, Length(S));
  end;
  Result := Result + S;
end;

{ The PATH Windows gives NEW terminals (user + machine, from the registry), not this installer's own copy. }
function RegistryPath(): String;
var
  U, M: String;
begin
  if not RegQueryStringValue(HKCU, 'Environment', 'Path', U) then U := '';
  if not RegQueryStringValue(HKLM, 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment', 'Path', M) then M := '';
  Result := ExpandEnvVars(M + ';' + U);
end;

function DirOnPath(Dir, PathList: String): Boolean;
var
  Entry: String;
  P: Integer;
begin
  Result := False;
  Dir := Lowercase(RemoveBackslashUnlessRoot(Dir));
  PathList := PathList + ';';
  while PathList <> '' do
  begin
    P := Pos(';', PathList);
    Entry := Lowercase(RemoveBackslashUnlessRoot(Trim(RemoveQuotes(Copy(PathList, 1, P - 1)))));
    PathList := Copy(PathList, P + 1, Length(PathList));
    if (Entry <> '') and (Entry = Dir) then begin Result := True; Exit; end;
  end;
end;

function FindClaude(): String;
var
  Candidate, Paths: String;
begin
  Result := '';
#ifdef TestNotReady
  { test builds only (ISCC /DTestNotReady): behave as if Claude Code isn't installed }
  Exit;
#endif
  Paths := RegistryPath() + ';' + GetEnv('PATH');
  Candidate := FileSearch('claude.exe', Paths);
  if Candidate = '' then
  begin
    Candidate := ExpandConstant('{%USERPROFILE}\.local\bin\claude.exe');
    if not FileExists(Candidate) then Candidate := '';
  end;
  if Candidate = '' then Candidate := FileSearch('claude.cmd', Paths);
  Result := Candidate;
end;

function JsonValue(Json, Key: String): String;
var
  P: Integer;
  Rest: String;
begin
  Result := '';
  P := Pos('"' + Key + '"', Json);
  if P = 0 then Exit;
  Rest := Copy(Json, P + Length(Key) + 2, Length(Json));
  P := Pos(':', Rest);
  Rest := Trim(Copy(Rest, P + 1, Length(Rest)));
  if (Rest <> '') and (Rest[1] = '"') then
  begin
    Rest := Copy(Rest, 2, Length(Rest));
    Result := Copy(Rest, 1, Pos('"', Rest) - 1);
  end
  else
  begin
    P := 1;
    while (P <= Length(Rest)) and (Rest[P] <> ',') and (Rest[P] <> '}') and (Rest[P] <> #13) and (Rest[P] <> #10) do P := P + 1;
    Result := Trim(Copy(Rest, 1, P - 1));
  end;
end;

{ "claude auth status" only reads the local sign-in state: it doesn't start a chat or use any Claude usage. }
procedure CheckSignIn();
var
  ResultCode, I: Integer;
  Output: TExecOutput;
  Json, Sub, Method: String;
  Ok: Boolean;
begin
  ClaudeSignedIn := False; ClaudePaid := False; PlanName := '';
  if ClaudePath = '' then Exit;
  if Lowercase(ExtractFileExt(ClaudePath)) = '.cmd' then
    Ok := ExecAndCaptureOutput(ExpandConstant('{cmd}'), '/c ""' + ClaudePath + '" auth status"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode, Output)
  else
    Ok := ExecAndCaptureOutput(ClaudePath, 'auth status', '', SW_HIDE, ewWaitUntilTerminated, ResultCode, Output);
  if not Ok then Exit;
  Json := '';
  for I := 0 to GetArrayLength(Output.StdOut) - 1 do Json := Json + Output.StdOut[I] + #10;
  ClaudeSignedIn := Lowercase(JsonValue(Json, 'loggedIn')) = 'true';
  if not ClaudeSignedIn then Exit;
  Sub := Lowercase(JsonValue(Json, 'subscriptionType'));
  Method := Lowercase(JsonValue(Json, 'authMethod'));
  if Sub <> '' then PlanName := Uppercase(Copy(Sub, 1, 1)) + Copy(Sub, 2, Length(Sub));
  { Free claude.ai accounts can't use Claude Code; paid plans, Console and API-key sign-ins can. }
  ClaudePaid := (Sub <> 'free') and ((Sub <> '') or (Method <> 'claude.ai'));
end;

function WebView2Installed(): Boolean;
var
  V: String;
begin
  Result :=
    (RegQueryStringValue(HKLM32, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\' + WebView2Guid, 'pv', V) and (V <> '') and (V <> '0.0.0.0')) or
    (RegQueryStringValue(HKLM64, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\' + WebView2Guid, 'pv', V) and (V <> '') and (V <> '0.0.0.0')) or
    (RegQueryStringValue(HKCU, 'Software\Microsoft\EdgeUpdate\Clients\' + WebView2Guid, 'pv', V) and (V <> '') and (V <> '0.0.0.0'));
end;

function TransparencyOn(): Boolean;
var
  D: Cardinal;
begin
  if RegQueryDWordValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Themes\Personalize', 'EnableTransparency', D) then
    Result := D <> 0
  else
    Result := True; { Windows' default }
end;

procedure SetRow(I: Integer; Status: Integer; Text: String);
begin
  { Status: 0 = ok, 1 = missing, 2 = warning (optional) }
  case Status of
    0: begin RowIcon[I].Caption := #$2714; RowIcon[I].Font.Color := ColorOk; end;
    1: begin RowIcon[I].Caption := #$2716; RowIcon[I].Font.Color := ColorBad; end;
  else
    begin RowIcon[I].Caption := '!'; RowIcon[I].Font.Color := ColorWarn; end;
  end;
  RowText[I].Caption := Text;
end;

procedure LayoutRequirements();
var
  I, Y: Integer;
begin
  Y := 0;
  for I := 0 to 4 do
  begin
    RowIcon[I].Top := Y;
    RowText[I].Top := Y;
    Y := Y + RowText[I].Height + ScaleY(5);
  end;
  Y := Y + ScaleY(6);
  SummaryLabel.Top := Y;
  Y := Y + SummaryLabel.Height + ScaleY(6);
  StepsLabel.Top := Y;
  Y := Y + StepsLabel.Height + ScaleY(8);
  CmdLabel.Top := Y;
  Y := Y + CmdLabel.Height + ScaleY(3);
  CmdEdit.Top := Y;
  CopyButton.Top := Y - ScaleY(1);
  CheckButton.Top := Y - ScaleY(1);
end;

procedure RunChecks();
var
  Steps, Cmd, CmdTitle: String;
  N: Integer;
begin
  SummaryLabel.Caption := 'Checking…';
  WizardForm.Refresh;

  ClaudePath := FindClaude();
  ClaudeInstalled := ClaudePath <> '';
  ClaudeOnPath := ClaudeInstalled and DirOnPath(ExtractFileDir(ClaudePath), RegistryPath());
  CheckSignIn();
  ClaudeReady := ClaudeInstalled and ClaudeSignedIn and ClaudePaid;

  if ClaudeInstalled then SetRow(0, 0, 'Claude Code is installed')
  else SetRow(0, 1, 'Claude Code is not installed');
  if ClaudeOnPath then SetRow(1, 0, 'Claude Code can be found from a terminal (it is on PATH)')
  else if ClaudeInstalled then SetRow(1, 1, 'Claude Code is not on PATH, so typing "claude" in a terminal won''t work yet')
  else SetRow(1, 1, 'Claude Code is not on PATH');
  if ClaudeSignedIn and ClaudePaid then
  begin
    if PlanName <> '' then SetRow(2, 0, 'Signed in to Claude (' + PlanName + ' plan)')
    else SetRow(2, 0, 'Signed in to Claude');
  end
  else if ClaudeSignedIn then SetRow(2, 1, 'Signed in, but with the free plan: OnRecord needs a paid plan (Pro or Max)')
  else SetRow(2, 1, 'Not signed in to Claude Code');
  if WebView2Installed() then SetRow(3, 0, 'Microsoft Edge WebView2 is installed (used to preview what Claude builds)')
  else SetRow(3, 2, 'WebView2 is missing: previews of pages Claude builds won''t show. Get the free "WebView2 Runtime" from Microsoft.');
  if TransparencyOn() then SetRow(4, 0, 'Windows transparency effects are on (for the glass look)')
  else SetRow(4, 2, 'Transparency effects are off, so the popup won''t look like glass. Turn on: Settings > Personalization > Colors > Transparency effects.');

  Steps := ''; N := 1; Cmd := ''; CmdTitle := '';
  if ClaudeReady then
  begin
    SummaryLabel.Caption := 'Claude Code is installed and signed in. You''re good to go.';
    SummaryLabel.Font.Color := ColorOk;
  end
  else
  begin
    SummaryLabel.Caption := 'Claude Code isn''t ready yet. OnRecord needs it, and a paid Claude plan (Pro or Max), to answer you:';
    SummaryLabel.Font.Color := ColorBad;
    if not ClaudeInstalled then
    begin
      Steps := Steps + IntToStr(N) + '. Open PowerShell (Start menu, type PowerShell), paste the command below and press Enter to install Claude Code.' + #13#10; N := N + 1;
      Cmd := InstallCommand; CmdTitle := 'Install command (PowerShell):';
    end;
    if ClaudeInstalled and not ClaudeOnPath then
    begin
      Steps := Steps + IntToStr(N) + '. Add Claude Code to PATH: paste the command below into PowerShell and press Enter.' + #13#10; N := N + 1;
      Cmd := PathCommand; CmdTitle := 'PATH fix (PowerShell):';
    end;
    if not (ClaudeSignedIn and ClaudePaid) then
    begin
      Steps := Steps + IntToStr(N) + '. Open a NEW PowerShell window, type claude, press Enter and sign in with your Claude Pro or Max account.' + #13#10; N := N + 1;
      if Cmd = '' then begin Cmd := 'claude'; CmdTitle := 'Then run this in a new PowerShell window:'; end;
    end;
    Steps := Steps + IntToStr(N) + '. Come back here and click Check again. You can also continue now, but OnRecord won''t be able to answer until this is done.';
  end;
  StepsLabel.Caption := Steps;
  StepsLabel.Visible := not ClaudeReady;
  CmdLabel.Caption := CmdTitle;
  CmdEdit.Text := Cmd;
  CmdLabel.Visible := Cmd <> '';
  CmdEdit.Visible := Cmd <> '';
  CopyButton.Visible := Cmd <> '';
  CopyButton.Caption := 'Copy';
  LayoutRequirements();
end;

procedure CheckAgainClick(Sender: TObject);
begin
  RunChecks();
end;

procedure CopyClick(Sender: TObject);
var
  F: String;
  ResultCode: Integer;
begin
  { Write the command to a temp file and let PowerShell put it on the clipboard (no quoting problems). }
  F := ExpandConstant('{tmp}\onrecord-copy.txt');
  if not SaveStringToFile(F, CmdEdit.Text, False) then Exit;
  if Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
          '-NoProfile -NonInteractive -Command "Get-Content -Raw -LiteralPath ''' + F + ''' | ForEach-Object { $_.TrimEnd() } | Set-Clipboard"',
          '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0) then
    CopyButton.Caption := 'Copied'
  else
    CopyButton.Caption := 'Failed';
  DeleteFile(F);
end;

{ ---------------------------------------------------------------- pages }

procedure CreateRequirementsPage();
var
  I, W: Integer;
begin
  ReqPage := CreateCustomPage(wpWelcome, 'Requirements', 'OnRecord talks to Claude through the Claude Code app on this PC.');
  W := ReqPage.SurfaceWidth;
  for I := 0 to 4 do
  begin
    RowIcon[I] := TNewStaticText.Create(ReqPage);
    RowIcon[I].Parent := ReqPage.Surface;
    RowIcon[I].Left := 0;
    RowIcon[I].Font.Style := [fsBold];
    RowIcon[I].Font.Size := 10;
    RowText[I] := TNewStaticText.Create(ReqPage);
    RowText[I].Parent := ReqPage.Surface;
    RowText[I].Left := ScaleX(22);
    RowText[I].Width := W - ScaleX(22);
    RowText[I].AutoSize := True;
    RowText[I].WordWrap := True;
  end;
  SummaryLabel := TNewStaticText.Create(ReqPage);
  SummaryLabel.Parent := ReqPage.Surface;
  SummaryLabel.Width := W; SummaryLabel.AutoSize := True; SummaryLabel.WordWrap := True;
  SummaryLabel.Font.Style := [fsBold];
  StepsLabel := TNewStaticText.Create(ReqPage);
  StepsLabel.Parent := ReqPage.Surface;
  StepsLabel.Width := W; StepsLabel.AutoSize := True; StepsLabel.WordWrap := True;
  CmdLabel := TNewStaticText.Create(ReqPage);
  CmdLabel.Parent := ReqPage.Surface;
  CmdLabel.Width := W;
  CheckButton := TNewButton.Create(ReqPage);
  CheckButton.Parent := ReqPage.Surface;
  CheckButton.Width := ScaleX(96); CheckButton.Height := ScaleY(25);
  CheckButton.Left := W - CheckButton.Width;
  CheckButton.Caption := 'Check again';
  CheckButton.OnClick := @CheckAgainClick;
  CopyButton := TNewButton.Create(ReqPage);
  CopyButton.Parent := ReqPage.Surface;
  CopyButton.Width := ScaleX(64); CopyButton.Height := ScaleY(25);
  CopyButton.Left := CheckButton.Left - CopyButton.Width - ScaleX(8);
  CopyButton.Caption := 'Copy';
  CopyButton.OnClick := @CopyClick;
  CmdEdit := TNewEdit.Create(ReqPage);
  CmdEdit.Parent := ReqPage.Surface;
  CmdEdit.Left := 0;
  CmdEdit.Width := CopyButton.Left - ScaleX(8);
  CmdEdit.ReadOnly := True;
  CmdEdit.Font.Name := 'Consolas';
end;

function HotkeyStatusText(): String;
var
  Free: Boolean;
begin
  Free := RegisterHotKey(0, 4711, MOD_ALT or MOD_NOREPEAT, VK_SPACE);
  if Free then UnregisterHotKey(0, 4711);
  if Free then
    Result := 'Alt+Space is free on this PC as far as Windows can tell.'
  else if CheckForMutexes('{#AppMutex}') then
    Result := 'Alt+Space is in use right now by the copy of OnRecord that is already running, which this install replaces.'
  else
    Result := 'Alt+Space is already taken by another app on this PC. OnRecord will tell you after installing; pick another shortcut in OnRecord''s settings or free it in the other app.';
end;

procedure CreateOptionsPage();
var
  W, Y: Integer;
begin
  OptPage := CreateCustomPage(ReqPage.ID, 'Options', 'Choose how OnRecord starts.');
  W := OptPage.SurfaceWidth;
  Y := 0;
  AutostartBox := TNewCheckBox.Create(OptPage);
  AutostartBox.Parent := OptPage.Surface;
  AutostartBox.Top := Y; AutostartBox.Width := W; AutostartBox.Height := ScaleY(18);
  AutostartBox.Caption := 'Start OnRecord when Windows starts';
  AutostartBox.Checked := True;
  Y := Y + ScaleY(20);
  AutostartHint := TNewStaticText.Create(OptPage);
  AutostartHint.Parent := OptPage.Surface;
  AutostartHint.Left := ScaleX(18); AutostartHint.Top := Y; AutostartHint.Width := W - ScaleX(18);
  AutostartHint.AutoSize := True; AutostartHint.WordWrap := True;
  AutostartHint.Font.Size := 8; AutostartHint.Font.Color := clGray;
  AutostartHint.Caption := 'Needed for Alt+Space to work any time. If this is off, you''ll have to open OnRecord yourself before Alt+Space works.';
  Y := Y + AutostartHint.Height + ScaleY(16);

  HotkeyNote := TNewStaticText.Create(OptPage);
  HotkeyNote.Parent := OptPage.Surface;
  HotkeyNote.Top := Y; HotkeyNote.Width := W; HotkeyNote.AutoSize := True; HotkeyNote.WordWrap := True;
  HotkeyNote.Caption := 'OnRecord opens with Alt+Space. If another app already uses Alt+Space (for example PowerToys Run, Copilot or ChatGPT''s desktop app), OnRecord''s shortcut won''t work until you change it in that app or in OnRecord''s settings.';
  Y := Y + HotkeyNote.Height + ScaleY(6);
  HotkeyStatus := TNewStaticText.Create(OptPage);
  HotkeyStatus.Parent := OptPage.Surface;
  HotkeyStatus.Top := Y; HotkeyStatus.Width := W; HotkeyStatus.AutoSize := True; HotkeyStatus.WordWrap := True;
  HotkeyStatus.Font.Style := [fsBold];
  HotkeyStatus.Caption := HotkeyStatusText();
  Y := Y + HotkeyStatus.Height + ScaleY(16);

  DesktopBox := TNewCheckBox.Create(OptPage);
  DesktopBox.Parent := OptPage.Surface;
  DesktopBox.Top := Y; DesktopBox.Width := W; DesktopBox.Height := ScaleY(18);
  DesktopBox.Caption := 'Create a desktop shortcut';
  DesktopBox.Checked := False;
end;

procedure InitializeWizard();
begin
  CreateRequirementsPage();
  CreateOptionsPage();
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = ReqPage.ID then RunChecks();
  if CurPageID = OptPage.ID then HotkeyStatus.Caption := HotkeyStatusText();
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = ReqPage.ID) and (not ClaudeReady) then
    Result := MsgBox('Claude Code isn''t installed and signed in yet, so OnRecord won''t be able to answer until that''s done.' + #13#10#13#10 +
                     'You can finish the steps on this page later and OnRecord will work from then on. Continue anyway?',
                     mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
end;

function WantAutostart(): Boolean;
begin
  { Silent installs (no wizard pages) keep the default: start with Windows. }
  if AutostartBox = nil then Result := True else Result := AutostartBox.Checked;
end;

function WantDesktopIcon(): Boolean;
begin
  if DesktopBox = nil then Result := False else Result := DesktopBox.Checked;
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo, MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result := 'OnRecord will be installed for you only (no administrator rights needed):' + NewLine + Space + ExpandConstant('{app}') + NewLine + NewLine;
  if WantAutostart() then Result := Result + 'Starts when Windows starts: yes' + NewLine
  else Result := Result + 'Starts when Windows starts: no' + NewLine;
  if WantDesktopIcon() then Result := Result + 'Desktop shortcut: yes' + NewLine
  else Result := Result + 'Desktop shortcut: no' + NewLine;
  if ClaudeReady then Result := Result + 'Claude Code: ready' + NewLine
  else Result := Result + 'Claude Code: not ready yet (see the Requirements page)' + NewLine;
end;

procedure StopRunningCopies();
var
  ResultCode: Integer;
begin
  { The tray app has no window for Restart Manager to close politely, so end it directly. }
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#AppExe}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(500);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopRunningCopies();
  Result := '';
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    if not WantAutostart() then
      RegDeleteValue(HKEY_CURRENT_USER, RunKey, '{#AppName}');
end;

{ ---------------------------------------------------------------- uninstall }

function InitializeUninstall(): Boolean;
begin
  StopRunningCopies();
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir, CompanyDir: String;
begin
  if CurUninstallStep = usUninstall then
    RegDeleteValue(HKEY_CURRENT_USER, RunKey, '{#AppName}');

  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{localappdata}\GlobalMayhem\OnRecord');
    CompanyDir := ExpandConstant('{localappdata}\GlobalMayhem');
    { Never delete the user's data without asking (and never in a silent uninstall). }
    if DirExists(DataDir) and (not UninstallSilent) then
      if MsgBox('Do you also want to delete your OnRecord data?' + #13#10#13#10 +
                'This removes your settings, colours, rules.txt and everything Claude saved in its workspace, ' +
                'including your artifacts and attachments:' + #13#10 + DataDir + #13#10#13#10 +
                'Choose No to keep it (for example if you plan to reinstall).',
                mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(DataDir, True, True, True);
    RemoveDir(CompanyDir); { only if empty }
    RemoveDir(ExpandConstant('{app}\..'));
  end;
end;
