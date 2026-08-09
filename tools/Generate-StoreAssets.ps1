[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$repoRoot = Split-Path -Parent $PSScriptRoot
$assetDirectory = Join-Path $repoRoot 'store\Assets'
New-Item -ItemType Directory -Path $assetDirectory -Force | Out-Null

function New-StoreLogo {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [int]$Size
    )

    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)

    try {
        $graphics.SmoothingMode =
            [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.TextRenderingHint =
            [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
        $graphics.Clear([System.Drawing.Color]::FromArgb(16, 19, 24))

        $inset = [Math]::Max(2, [Math]::Round($Size * 0.12))
        $diameter = $Size - (2 * $inset)
        $accentBrush = [System.Drawing.SolidBrush]::new(
            [System.Drawing.Color]::FromArgb(45, 125, 202))

        try {
            $graphics.FillEllipse(
                $accentBrush,
                $inset,
                $inset,
                $diameter,
                $diameter)
        }
        finally {
            $accentBrush.Dispose()
        }

        $fontSize = [Math]::Max(9, [Math]::Round($Size * 0.31))
        $font = [System.Drawing.Font]::new(
            'Segoe UI',
            $fontSize,
            [System.Drawing.FontStyle]::Bold,
            [System.Drawing.GraphicsUnit]::Pixel)
        $textBrush = [System.Drawing.SolidBrush]::new(
            [System.Drawing.Color]::White)
        $format = [System.Drawing.StringFormat]::new()

        try {
            $format.Alignment = [System.Drawing.StringAlignment]::Center
            $format.LineAlignment = [System.Drawing.StringAlignment]::Center
            $graphics.DrawString(
                'WQ',
                $font,
                $textBrush,
                [System.Drawing.RectangleF]::new(0, 0, $Size, $Size),
                $format)
        }
        finally {
            $format.Dispose()
            $textBrush.Dispose()
            $font.Dispose()
        }

        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

$assets = @(
    @{ Name = 'StoreLogo.png'; Size = 50 },
    @{ Name = 'Square44x44Logo.png'; Size = 44 },
    @{ Name = 'Square150x150Logo.png'; Size = 150 }
)

foreach ($asset in $assets) {
    New-StoreLogo `
        -Path (Join-Path $assetDirectory $asset.Name) `
        -Size $asset.Size
}

Write-Host "Generated $($assets.Count) Store assets in $assetDirectory"
