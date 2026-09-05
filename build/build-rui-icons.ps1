<#
    Rebuilds the <bitmaps> block in OtterLogic.rui from assets/icons.

    Rhino toolbar icons cannot be loaded from a file at runtime; they are
    embedded in the .rui as base64. One horizontal strip per size, with each
    macro's bitmap_id naming the slot it occupies. Rhino's own default.rui
    carries no bitmaps at all - every bitmap_id there indexes Rhino's internal
    icon library, which a third party cannot reference.

    Run this after changing anything in assets/icons, then rebuild.

    Usage:
        pwsh build/build-rui-icons.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$repoRoot = Split-Path -Parent $PSScriptRoot
$iconDir  = Join-Path $repoRoot 'assets\icons'
$rui      = Join-Path $repoRoot 'src\OtterLogic.Rhino\UI\OtterLogic.rui'

# Slot order is the strip order, and the guids are referenced by macro_item
# bitmap_id in the .rui. Adding a tool means adding a row here and a matching
# bitmap_id on its macro.
$slots = @(
    @{ Name = 'truss2d'; Guid = '0b3809d9-6ce0-4f3f-b427-8d596b600b0d' }
)

foreach ($slot in $slots) {
    $path = Join-Path $iconDir "$($slot.Name).png"
    if (-not (Test-Path $path)) { throw "Missing icon: $path" }
}

function New-Strip([int]$size) {
    $strip = New-Object System.Drawing.Bitmap(($size * $slots.Count), $size)
    $g = [System.Drawing.Graphics]::FromImage($strip)
    try {
        $g.InterpolationMode = 'HighQualityBicubic'
        $g.PixelOffsetMode = 'HighQuality'
        $g.Clear([System.Drawing.Color]::Transparent)
        for ($i = 0; $i -lt $slots.Count; $i++) {
            $src = [System.Drawing.Image]::FromFile((Join-Path $iconDir "$($slots[$i].Name).png"))
            try { $g.DrawImage($src, ($i * $size), 0, $size, $size) } finally { $src.Dispose() }
        }
    }
    finally { $g.Dispose() }

    $ms = New-Object System.IO.MemoryStream
    try {
        $strip.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        return [Convert]::ToBase64String($ms.ToArray())
    }
    finally { $ms.Dispose(); $strip.Dispose() }
}

function New-Block([string]$tag, [int]$size) {
    $lines = @("    <$tag item_width=`"$size`" item_height=`"$size`">")
    for ($i = 0; $i -lt $slots.Count; $i++) {
        $lines += "      <bitmap_item guid=`"$($slots[$i].Guid)`" index=`"$i`" />"
    }
    $lines += "      <bitmap>$(New-Strip $size)</bitmap>"
    $lines += "    </$tag>"
    return $lines -join "`n"
}

$block = @(
    '  <bitmaps>'
    (New-Block 'small_bitmap'  16)
    (New-Block 'normal_bitmap' 24)
    (New-Block 'large_bitmap'  32)
    '  </bitmaps>'
) -join "`n"

$text = [System.IO.File]::ReadAllText($rui) -replace "`r`n", "`n"
$pattern = '(?s)  <bitmaps>.*?</bitmaps>'
if ($text -notmatch $pattern) { throw "No <bitmaps> block found in $rui" }

$text = [regex]::Replace($text, $pattern, [System.Text.RegularExpressions.MatchEvaluator] { param($m) $block })

# UTF-8 with a BOM, matching the encoding Rhino writes its own .rui files in.
$utf8Bom = New-Object System.Text.UTF8Encoding($true)
[System.IO.File]::WriteAllText($rui, $text, $utf8Bom)

Write-Host "Rebuilt $($slots.Count) icons at 16/24/32 into $rui" -ForegroundColor Green
Write-Host 'Now run: dotnet build OtterLogic.slnx' -ForegroundColor Cyan
