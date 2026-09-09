<#
    Builds Release and packs dist/ into a .yak package.

    A yak package holds the .rhp, the .gha and the shared Core.dll in one
    folder. Rhino registers the plug-in from it and Grasshopper scans it for
    .gha files, which is exactly the layout dist/ already has - that is why
    both projects build there.

    Usage:
        pwsh build/pack.ps1              # build the package
        pwsh build/pack.ps1 -Push        # build, then publish to the server
#>
param(
    [switch]$Push
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$dist     = Join-Path $repoRoot 'dist'
$yak      = 'C:\Program Files\Rhino 8\System\Yak.exe'

if (-not (Test-Path $yak)) { throw "Yak not found at $yak" }

# The version lives in Directory.Build.props and nowhere else. yak build takes
# --version, so manifest.yml never has to be kept in step by hand.
$props   = Join-Path $repoRoot 'Directory.Build.props'
$version = ([xml](Get-Content $props)).Project.PropertyGroup.Version | Where-Object { $_ }
if (-not $version) { throw "No <Version> found in $props" }
Write-Host "Version $version" -ForegroundColor Cyan

Write-Host 'Building Release...' -ForegroundColor Cyan
dotnet build (Join-Path $repoRoot 'OtterLogic.slnx') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

Copy-Item (Join-Path $PSScriptRoot 'manifest.yml') $dist -Force

Write-Host 'Packing...' -ForegroundColor Cyan
Push-Location $dist
try {
    # Yak leaves earlier packages where it found them, and the one this run has
    # to push is then whichever file sorts newest - a coin toss worth removing.
    Get-ChildItem -Filter '*.yak' | Remove-Item -Force

    & $yak build --platform win --version $version
    if ($LASTEXITCODE -ne 0) { throw 'yak build failed.' }

    $package = Get-ChildItem -Filter '*.yak' | Select-Object -First 1
    Write-Host "Built $($package.Name)" -ForegroundColor Green

    # The manifest yak writes is not the one on disk: it drops in the platform
    # and whatever plug-in ids it managed to read out of the binaries. A missing
    # guid installs fine here and does nothing on a machine that has never seen
    # the plug-in, so print the keywords rather than trust them.
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($package.FullName)
    try {
        $entry  = $zip.Entries | Where-Object { $_.FullName -eq 'manifest.yml' }
        $reader = New-Object System.IO.StreamReader($entry.Open())
        $guids  = ($reader.ReadToEnd() -split "`n") | Where-Object { $_ -match 'guid:' }
        $reader.Dispose()
    }
    finally { $zip.Dispose() }

    Write-Host "Plug-in ids in the package:" -ForegroundColor Cyan
    $guids | ForEach-Object { Write-Host "  $($_.Trim(' ', '-'))" }
    if ($guids.Count -lt 2) {
        Write-Warning 'Expected two ids - the Rhino plug-in and the Grasshopper assembly.'
    }

    if ($Push) {
        # Requires `yak login` once beforehand.
        & $yak push $package.Name
        if ($LASTEXITCODE -ne 0) { throw 'yak push failed.' }
        Write-Host 'Pushed.' -ForegroundColor Green
    }
}
finally {
    Pop-Location
}
