# BranteGameLocator.ps1 - Shared game-install resolution for setup-bepinex.ps1 and
# build.ps1. Dot-source this file, then call Resolve-BranteGame / Test-BranteGameDir.
# Resolution order: BRANTE_GAME env var, then Steam (registry roots + every library in
# libraryfolders.vdf), then GOG (uninstall registry entries + default Galaxy paths).

$BranteGameFolderName = "The Life and Suffering of Sir Brante"
$BranteExeName = "The Life and Suffering of Sir Brante.exe"
$BranteManagedRelPath = "The Life and Suffering of Sir Brante_Data\Managed\Assembly-CSharp.dll"
$BranteDefaultSteamPath = "C:\Program Files (x86)\Steam\steamapps\common\$BranteGameFolderName"

function Normalize-BrantePath {
    param([string]$Path)

    if (-not $Path) { return $null }
    $normalized = $Path.Trim().Trim('"')
    if ([string]::IsNullOrWhiteSpace($normalized)) { return $null }
    return $normalized
}

function Test-BranteGameDir {
    param([string]$Path)

    $game = Normalize-BrantePath $Path
    if (-not $game) { return $false }

    return (Test-Path (Join-Path $game $BranteExeName)) -and
        (Test-Path (Join-Path $game $BranteManagedRelPath))
}

function Get-SteamLibraryPathsFromVdf {
    param([string]$Content)

    if (-not $Content) { return @() }

    $paths = @()
    [regex]::Matches($Content, '"path"\s+"([^"]+)"') | ForEach-Object {
        $paths += ($_.Groups[1].Value -replace '\\\\', '\')
    }
    return $paths
}

function Get-SteamRoots {
    $roots = @()

    $hkcuSteam = (Get-ItemProperty -Path "HKCU:\Software\Valve\Steam" -Name SteamPath -ErrorAction SilentlyContinue).SteamPath
    if ($hkcuSteam) { $roots += ($hkcuSteam -replace '/', '\') }

    $hklmSteam = (Get-ItemProperty -Path "HKLM:\SOFTWARE\WOW6432Node\Valve\Steam" -Name InstallPath -ErrorAction SilentlyContinue).InstallPath
    if ($hklmSteam) { $roots += $hklmSteam }

    $roots += "C:\Program Files (x86)\Steam"

    $seen = @{}
    foreach ($root in $roots) {
        $normalized = Normalize-BrantePath $root
        if (-not $normalized) { continue }
        $key = $normalized.ToLowerInvariant()
        if (-not $seen.ContainsKey($key)) {
            $seen[$key] = $true
            $normalized
        }
    }
}

function Get-SteamBranteCandidates {
    param([string[]]$SteamRoots)

    $candidates = @()
    foreach ($steam in $SteamRoots) {
        $root = Normalize-BrantePath $steam
        if (-not $root) { continue }

        $candidates += (Join-Path $root "steamapps\common\$BranteGameFolderName")

        $libraryFolders = Join-Path $root "steamapps\libraryfolders.vdf"
        if (Test-Path $libraryFolders) {
            $content = Get-Content $libraryFolders -Raw
            foreach ($library in Get-SteamLibraryPathsFromVdf $content) {
                $candidates += (Join-Path $library "steamapps\common\$BranteGameFolderName")
            }
        }
    }

    return $candidates
}

function Get-GogInstallPathsFromRegistry {
    $candidates = @()
    $roots = @(
        "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
        "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        "HKCU:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    )

    foreach ($root in $roots) {
        if (-not (Test-Path $root)) { continue }

        Get-ChildItem $root -ErrorAction SilentlyContinue | ForEach-Object {
            $app = Get-ItemProperty -Path $_.PSPath -ErrorAction SilentlyContinue
            if (-not $app) { return }

            $displayName = [string]$app.DisplayName
            if (-not $displayName -or -not $displayName.ToLowerInvariant().Contains("brante")) {
                return
            }

            if ($app.InstallLocation) { $candidates += $app.InstallLocation }
            if ($app.InstallDir) { $candidates += $app.InstallDir }
        }
    }

    return $candidates
}

function Get-GogBranteCandidates {
    param([string[]]$GogInstallPaths)

    $candidates = @()
    if ($GogInstallPaths) {
        $candidates += $GogInstallPaths
    } else {
        $candidates += Get-GogInstallPathsFromRegistry
    }

    $candidates += "C:\GOG Games\$BranteGameFolderName"
    $candidates += "C:\Program Files (x86)\GOG Galaxy\Games\$BranteGameFolderName"
    $candidates += "C:\Program Files\GOG Galaxy\Games\$BranteGameFolderName"

    return $candidates
}

function Add-BranteCandidate {
    param(
        [System.Collections.ArrayList]$Candidates,
        [hashtable]$Seen,
        [string]$Path
    )

    $normalized = Normalize-BrantePath $Path
    if (-not $normalized) { return }

    $key = $normalized.ToLowerInvariant()
    if ($Seen.ContainsKey($key)) { return }

    $Seen[$key] = $true
    [void]$Candidates.Add($normalized)
}

function Get-BranteGameCandidates {
    param(
        [string]$ManualGame = $env:BRANTE_GAME,
        [string[]]$SteamRoots = $(Get-SteamRoots),
        [string[]]$GogInstallPaths = $null
    )

    $candidates = New-Object System.Collections.ArrayList
    $seen = @{}

    Add-BranteCandidate $candidates $seen $ManualGame

    foreach ($candidate in Get-SteamBranteCandidates $SteamRoots) {
        Add-BranteCandidate $candidates $seen $candidate
    }

    foreach ($candidate in Get-GogBranteCandidates $GogInstallPaths) {
        Add-BranteCandidate $candidates $seen $candidate
    }

    return @($candidates)
}

function Resolve-BranteGame {
    param(
        [string]$ManualGame = $env:BRANTE_GAME,
        [string[]]$SteamRoots = $(Get-SteamRoots),
        [string[]]$GogInstallPaths = $null
    )

    $manual = Normalize-BrantePath $ManualGame
    if ($manual) {
        return $manual
    }

    foreach ($candidate in Get-BranteGameCandidates -ManualGame $null -SteamRoots $SteamRoots -GogInstallPaths $GogInstallPaths) {
        if (Test-BranteGameDir $candidate) {
            return $candidate
        }
    }

    return $BranteDefaultSteamPath
}

function Test-BranteGameRunning {
    return $null -ne (Get-Process -Name "The Life and Suffering of Sir Brante" -ErrorAction SilentlyContinue)
}
