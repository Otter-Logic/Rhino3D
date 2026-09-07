<#
    Drops Rhino's cached copy of this plug-in's toolbar, so the next start
    re-reads OtterLogic.rui.

    Rhino imports a plug-in .rui once, converts it into its own settings under
    %APPDATA%\McNeel\Rhinoceros\<ver>\settings\Scheme__*\<plug-in>_<guid>.xml,
    and reads that from then on. Edit the .rui and rebuild and nothing changes:
    new buttons, renamed commands and edited tooltips all stay stale. Deleting
    the cache is the whole fix.

    Bumping major_version on the tool_bar_group_item is the documented way to
    push a change, and is still right for a shipped install. It is wrong here:
    Rhino imports the group again under a new identity, leaving you with
    "OtterLogic Toolbar Group 01", then 02, old copies still listed.

    The file is a cache, rebuilt from the .rui, so there is nothing to back up.
    Nothing below names a button or a command - the cache is found through the
    guid on <RhinoUI>, so this keeps working as buttons are added.

    If the toolbar goes missing entirely and will not come back, that is a
    different problem: Rhino writes a <removed_item> record into containers.xml
    when a toolbar is closed. Try the Toolbar command first; failing that the
    record has to come out of containers.xml by hand.

    Usage:
        pwsh build/reset-toolbar.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$rui = Join-Path (Split-Path -Parent $PSScriptRoot) 'src\OtterLogic.Rhino\UI\OtterLogic.rui'

if (@(Get-Process Rhino -ErrorAction SilentlyContinue).Count -gt 0) {
    throw 'Rhino is running. Close it first - it rewrites these on exit, undoing this.'
}

if (-not (Test-Path $rui)) { throw "No .rui at $rui" }

if ([System.IO.File]::ReadAllText($rui) -notmatch '<RhinoUI[^>]*\bguid="([0-9a-fA-F-]{36})"') {
    throw "No guid on the <RhinoUI> element in $rui"
}
$guid = $Matches[1]

# One settings folder per Rhino version per scheme, so this does not have to
# know which Rhino you happen to be running today.
$cached = @(
    Get-ChildItem (Join-Path $env:APPDATA 'McNeel\Rhinoceros') -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^\d+\.\d+$' } |
        ForEach-Object { Get-ChildItem $_.FullName -Filter "*_$guid.xml" -Recurse -ErrorAction SilentlyContinue }
)

foreach ($file in $cached) {
    Remove-Item $file.FullName -Force
    Write-Host "Dropped $($file.FullName)" -ForegroundColor Green
}

if ($cached.Count -eq 0) {
    Write-Host 'Nothing cached - Rhino will read the .rui fresh anyway.' -ForegroundColor Yellow
}

Write-Host 'Now: dotnet build OtterLogic.slnx, then start Rhino.' -ForegroundColor Cyan
