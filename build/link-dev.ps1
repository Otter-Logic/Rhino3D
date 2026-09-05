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
$version    = '0.1.0'
$packages   = Join-Path $env:APPDATA 'McNeel\Rhinoceros\packages\8.0'
$packageDir = Join-Path $packages $name
$versionDir = Join-Path $packageDir $version

if (@(Get-Process Rhino -ErrorAction SilentlyContinue).Count -gt 0) {
    throw 'Rhino is running. Close it first - it holds the .rhp and .gha open.'
}

if ($Unlink) {
    if (Test-Path $packageDir) {
        # Remove the junction itself, never its target.
        $link = Get-Item $versionDir -ErrorAction SilentlyContinue
        if ($null -ne $link -and $link.LinkType) { $link.Delete() }
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
