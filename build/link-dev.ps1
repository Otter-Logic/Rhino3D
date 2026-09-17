<#
    Links dist/ into Rhino as a development package.

    Rhino scans %APPDATA%\McNeel\Rhinoceros\packages\8.0\<name>\<version>\ and
    loads any .rhp it finds there; Grasshopper scans the same folders for .gha.
    That single location covers both front-ends, with no PlugInManager step and
    no Grasshopper developer-settings step.

    Rather than copying, this points a directory junction at dist/, so every
    `dotnet build` is picked up by the next Rhino start. Close Rhino before
    rebuilding - it holds both files open while running.

    Usage:
        pwsh build/link-dev.ps1
        pwsh build/link-dev.ps1 -Unlink
#>
param(
    [switch]$Unlink
)

$ErrorActionPreference = 'Stop'

$repoRoot   = Split-Path -Parent $PSScriptRoot
$dist       = Join-Path $repoRoot 'dist'
$name       = 'OtterLogic'

# Read from Directory.Build.props, the same single source pack.ps1 uses. Held
# here as a literal it goes stale the moment the version is bumped, and the
# failure is silent: the link still points at a folder named after the old
# version, so Rhino keeps loading it and the dev loop looks fine while the
# package it is meant to mirror has moved on.
$props      = Join-Path $repoRoot 'Directory.Build.props'
$version    = ([xml](Get-Content $props)).Project.PropertyGroup.Version | Where-Object { $_ }
if (-not $version) { throw "No <Version> found in $props" }

$packages   = Join-Path $env:APPDATA 'McNeel\Rhinoceros\packages\8.0'
$packageDir = Join-Path $packages $name
$versionDir = Join-Path $packageDir $version

if (@(Get-Process Rhino -ErrorAction SilentlyContinue).Count -gt 0) {
    throw 'Rhino is running. Close it first - it holds the .rhp and .gha open.'
}

if ($Unlink) {
    if (Test-Path $packageDir) {
        # Remove the junctions themselves, never their targets - and every one
        # in here, not just this version's. Bumping <Version> leaves the
        # previous version's link behind, and Remove-Item -Recurse in Windows
        # PowerShell will follow a junction it finds and empty the repo's dist/
        # rather than just unlinking it.
        Get-ChildItem $packageDir -Force |
            Where-Object { $_.LinkType } |
            ForEach-Object { $_.Delete() }
        Remove-Item $packageDir -Recurse -Force
        Write-Host "Unlinked $packageDir" -ForegroundColor Green
    }
    else {
        Write-Host 'Nothing linked.' -ForegroundColor Yellow
    }
    return
}

if (-not (Test-Path $dist)) { throw "No dist folder. Run: dotnet build OtterLogic.slnx" }

# Rhino wants a manifest beside the binaries, and the version in a sibling file.
Copy-Item (Join-Path $PSScriptRoot 'manifest.yml') $dist -Force

if (Test-Path $versionDir) {
    $existing = Get-Item $versionDir
    if ($existing.LinkType) { $existing.Delete() } else { Remove-Item $versionDir -Recurse -Force }
}

New-Item -ItemType Directory -Path $packageDir -Force | Out-Null
Set-Content -Path (Join-Path $packageDir 'manifest.txt') -Value $version -Encoding utf8 -NoNewline

try {
    New-Item -ItemType Junction -Path $versionDir -Target $dist | Out-Null
    Write-Host "Linked $versionDir -> $dist" -ForegroundColor Green
}
catch {
    # Junctions need the target on a local NTFS volume; fall back to a copy.
    Write-Host "Junction failed ($($_.Exception.Message)); copying instead." -ForegroundColor Yellow
    New-Item -ItemType Directory -Path $versionDir -Force | Out-Null
    Copy-Item (Join-Path $dist '*') $versionDir -Recurse -Force
    Write-Host "Copied dist -> $versionDir (re-run after each build)" -ForegroundColor Green
}

Write-Host ''
Write-Host 'Start Rhino. OtterLogic loads as a package - no PlugInManager or' -ForegroundColor Cyan
Write-Host 'GrasshopperDeveloperSettings step needed.' -ForegroundColor Cyan
