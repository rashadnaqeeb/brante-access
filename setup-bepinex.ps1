# setup-bepinex.ps1 - Install the vendored BepInEx (third_party/bepinex zip) into the
# game folder. Idempotent: safe to re-run (e.g. after a game update wipes it). Unity
# 2018.3 Mono needs no entrypoint tweak, so the upstream default doorstop config is
# used as-is. After this, run build.ps1 to build and deploy the mod.

$ErrorActionPreference = "Stop"

. "$PSScriptRoot\scripts\BranteGameLocator.ps1"

# --- Locate the game install ---
# BRANTE_GAME env var wins; otherwise auto-detect Steam and GOG installs.
$Game = Resolve-BranteGame
if (-not (Test-BranteGameDir $Game)) {
    Write-Host "ERROR: The Life and Suffering of Sir Brante not found at: $Game" -ForegroundColor Red
    Write-Host "Set the BRANTE_GAME environment variable to the game folder." -ForegroundColor Red
    exit 1
}

# winhttp.dll (the loader proxy) is locked while the game runs, so the copy would fail.
if (Test-BranteGameRunning) {
    Write-Host "ERROR: the game is running. Close it, then re-run this script." -ForegroundColor Red
    exit 1
}

$Zip = Get-Item "$PSScriptRoot\third_party\bepinex\BepInEx_win_x64_*.zip" -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $Zip) {
    Write-Host "ERROR: vendored BepInEx zip not found under $PSScriptRoot\third_party\bepinex" -ForegroundColor Red
    exit 1
}

Write-Host "Installing BepInEx ($($Zip.Name)) into $Game ..." -ForegroundColor Cyan

# The zip's layout matches the game folder (winhttp.dll + doorstop_config.ini at the
# root, core dlls under BepInEx\core), so it extracts straight into the install.
Expand-Archive -Path $Zip.FullName -DestinationPath $Game -Force
New-Item -ItemType Directory -Path "$Game\BepInEx\plugins" -Force | Out-Null

Write-Host ""
Write-Host "BepInEx installed. Now run build.ps1 to build and deploy the mod." -ForegroundColor Cyan
Write-Host "(First game launch after install generates BepInEx\config\BepInEx.cfg and LogOutput.log.)" -ForegroundColor DarkGray
