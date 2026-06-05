<#
  REPOBot one-click builder for the Steam version of R.E.P.O.

  Auto-detects your Steam R.E.P.O. install, makes sure the .NET SDK is present
  (offers to install it), builds the mod, and copies REPOBot.dll into
  BepInEx\plugins\REPOBot.

  Usage (normally just double-click build.bat):
    powershell -ExecutionPolicy Bypass -File build.ps1
    powershell -ExecutionPolicy Bypass -File build.ps1 -GameDir "D:\Games\REPO"
#>

param(
    [string]$GameDir = $env:REPO_DIR
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

function Info($m)  { Write-Host "[REPOBot] $m" -ForegroundColor Cyan }
function Good($m)  { Write-Host "[REPOBot] $m" -ForegroundColor Green }
function Warn($m)  { Write-Host "[REPOBot] $m" -ForegroundColor Yellow }
function Fail($m)  { Write-Host "[REPOBot] $m" -ForegroundColor Red; exit 1 }

Write-Host ""
Info "REPOBot one-click build starting..."
Write-Host ""

# ---------------------------------------------------------------------------
# 1. Locate the Steam R.E.P.O. install
# ---------------------------------------------------------------------------
function Test-RepoDir($dir) {
    if ([string]::IsNullOrWhiteSpace($dir)) { return $false }
    return (Test-Path (Join-Path $dir "REPO.exe"))
}

function Find-SteamRoot {
    foreach ($key in @("HKCU:\Software\Valve\Steam", "HKLM:\SOFTWARE\WOW6432Node\Valve\Steam", "HKLM:\SOFTWARE\Valve\Steam")) {
        try {
            $p = (Get-ItemProperty -Path $key -ErrorAction Stop).SteamPath
            if ($p) { return ($p -replace '/', '\') }
        } catch { }
    }
    foreach ($d in @("C:\Program Files (x86)\Steam", "C:\Program Files\Steam")) {
        if (Test-Path $d) { return $d }
    }
    return $null
}

function Find-RepoViaSteam {
    $steam = Find-SteamRoot
    if (-not $steam) { return $null }

    # Steam can install games across several "library folders".
    $libs = @($steam)
    $vdf = Join-Path $steam "steamapps\libraryfolders.vdf"
    if (Test-Path $vdf) {
        foreach ($line in Get-Content $vdf) {
            if ($line -match '"path"\s+"(.+?)"') {
                $libs += ($matches[1] -replace '\\\\', '\')
            }
        }
    }
    foreach ($lib in ($libs | Select-Object -Unique)) {
        $candidate = Join-Path $lib "steamapps\common\REPO"
        if (Test-RepoDir $candidate) { return $candidate }
    }
    return $null
}

if (-not (Test-RepoDir $GameDir)) {
    Info "Looking for your Steam R.E.P.O. install..."
    $GameDir = Find-RepoViaSteam
}

if (-not (Test-RepoDir $GameDir)) {
    Warn "Could not find R.E.P.O. automatically."
    Warn "In Steam: right-click R.E.P.O. -> Manage -> Browse local files, copy that folder path."
    $GameDir = Read-Host "Paste the R.E.P.O. folder path here"
}

if (-not (Test-RepoDir $GameDir)) {
    Fail "That folder doesn't contain REPO.exe. Aborting."
}
Good "Found R.E.P.O. at: $GameDir"

# ---------------------------------------------------------------------------
# 2. Make sure the .NET SDK is available
# ---------------------------------------------------------------------------
function Have-Dotnet {
    try { $null = (& dotnet --version) 2>$null; return ($LASTEXITCODE -eq 0) } catch { return $false }
}

if (-not (Have-Dotnet)) {
    Warn ".NET SDK not found - it's needed to build the mod (one-time install)."
    $haveWinget = $false
    try { $null = (& winget --version) 2>$null; $haveWinget = ($LASTEXITCODE -eq 0) } catch { }

    if ($haveWinget) {
        $ans = Read-Host "Install the .NET 8 SDK now via winget? [Y/n]"
        if ($ans -eq "" -or $ans -match '^[Yy]') {
            Info "Installing Microsoft.DotNet.SDK.8 ..."
            winget install --id Microsoft.DotNet.SDK.8 -e --accept-source-agreements --accept-package-agreements
            $env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" +
                        [System.Environment]::GetEnvironmentVariable("Path","User")
        }
    }

    if (-not (Have-Dotnet)) {
        Warn "Please install the .NET SDK, then run this again:"
        Warn "    https://dotnet.microsoft.com/download/dotnet/8.0"
        Fail "Aborting until the .NET SDK is installed."
    }
}
Good ".NET SDK: $(& dotnet --version)"

# ---------------------------------------------------------------------------
# 3. Build
# ---------------------------------------------------------------------------
Info "Building REPOBot (this can take a minute the first time)..."
$proj = Join-Path $root "src\REPOBot\REPOBot.csproj"
& dotnet build $proj -c Release -p:REPOGameDir="$GameDir" --nologo
if ($LASTEXITCODE -ne 0) { Fail "Build failed - see the messages above." }

$dll = Join-Path $root "src\REPOBot\bin\Release\netstandard2.1\REPOBot.dll"
if (-not (Test-Path $dll)) { Fail "Build reported success but REPOBot.dll wasn't found." }
Good "Built: $dll"

# ---------------------------------------------------------------------------
# 4. Install into BepInEx
# ---------------------------------------------------------------------------
$bepinex = Join-Path $GameDir "BepInEx"
if (-not (Test-Path $bepinex)) {
    Warn "BepInEx isn't installed in your game folder yet."
    Warn "Install 'BepInExPack' first (Thunderstore, or r2modman/Thunderstore Mod Manager), then re-run."
    Warn "Your built DLL is here for now: $dll"
    exit 0
}

$dest = Join-Path $bepinex "plugins\REPOBot"
New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item $dll $dest -Force
Good "Installed to: $dest"
Write-Host ""
Good "Done! Launch R.E.P.O. and press F8 in-game to toggle the bot."
Good "  F9 = switch SafeCollect / Speedrun    F10 = panic (give control back)    F11 = clear best time"
Write-Host ""
