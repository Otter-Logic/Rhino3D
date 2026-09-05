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

Write-Host 'Building Release...' -ForegroundColor Cyan
dotnet build (Join-Path $repoRoot 'OtterLogic.slnx') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

Copy-Item (Join-Path $PSScriptRoot 'manifest.yml') $dist -Force

Write-Host 'Packing...' -ForegroundColor Cyan
Push-Location $dist
try {
    & $yak build --platform win
    if ($LASTEXITCODE -ne 0) { throw 'yak build failed.' }

    $package = Get-ChildItem -Filter '*.yak' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    Write-Host "Built $($package.Name)" -ForegroundColor Green

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
