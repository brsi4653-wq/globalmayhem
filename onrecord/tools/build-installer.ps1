# Builds dist\OnRecord-Setup-<version>.exe. Run from the code folder:  powershell -File tools\build-installer.ps1
# 1) publishes the app self-contained for x64 (no .NET install needed on the target PC) into publish\win-x64
# 2) compiles installer\OnRecord.iss with Inno Setup 6 (per-user install: winget install JRSoftware.InnoSetup --scope user)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
Set-Location $root

[xml]$proj = Get-Content "OnRecord\OnRecord.csproj"
$version = ($proj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { throw "No <Version> in OnRecord.csproj" }

$publish = Join-Path $root "publish\win-x64"
if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }
dotnet publish OnRecord\OnRecord.csproj -c Release -r win-x64 --self-contained true -p:DebugType=none -o $publish -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
foreach ($f in "OnRecord.exe", "rules.txt", "Assets\onrecord.ico", "Assets\onrecord-tray.ico", "Assets\logo-64.png") {
    if (-not (Test-Path (Join-Path $publish $f))) { throw "publish output is missing $f" }
}

$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
        Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 not found. Install it with: winget install JRSoftware.InnoSetup --scope user" }
& $iscc /Q "/DAppVersion=$version" "/DPublishDir=$publish" "installer\OnRecord.iss"
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed" }
Get-Item "dist\OnRecord-Setup-$version.exe"
