param(
    [string]$OutputDir = "",
    [switch]$KeepIntermediate
)

$ErrorActionPreference = "Stop"

function Resolve-Tool {
    param([string]$Name)

    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if (-not $command) {
        throw "Required tool '$Name' was not found on PATH."
    }

    return $command.Source
}

function Escape-PdfText {
    param([string]$Value)

    return ($Value -replace "\\", "\\") -replace "\(", "\(" -replace "\)", "\)"
}

function Add-PdfText {
    param(
        [System.Text.StringBuilder]$Content,
        [double]$X,
        [double]$Y,
        [double]$Size,
        [string]$Text
    )

    [void]$Content.AppendLine("BT /F1 $Size Tf $X $Y Td ($(Escape-PdfText $Text)) Tj ET")
}

function Add-PdfLine {
    param(
        [System.Text.StringBuilder]$Content,
        [double]$X1,
        [double]$Y1,
        [double]$X2,
        [double]$Y2
    )

    [void]$Content.AppendLine("$X1 $Y1 m $X2 $Y2 l S")
}

function New-SyntheticLabReportPdf {
    param([string]$Path)

    $content = [System.Text.StringBuilder]::new()
    [void]$content.AppendLine("0.55 w")
    Add-PdfText $content 48 795 18 "Longevity World Cup Proof Quality Regression"
    Add-PdfText $content 48 774 9 "Synthetic lab-style report. This fixture exercises small text, dense table rows, and right-aligned reference ranges."
    Add-PdfText $content 48 758 8 "Patient: Test Athlete    Sample date: 2026-05-19    Report ID: LWC-PROOF-QUALITY-001"

    $left = 42
    $top = 720
    $rowHeight = 17
    $cols = @(42, 202, 282, 374, 506, 553)
    $rows = @(
        @("Marker", "Result", "Unit", "Reference interval", "Flag"),
        @("Albumin", "44.2", "g/L", "35.0 - 52.0", ""),
        @("Creatinine", "82", "umol/L", "59 - 104", ""),
        @("Glucose", "5.1", "mmol/L", "3.9 - 5.5", ""),
        @("C-reactive protein", "0.38", "mg/L", "0.00 - 5.00", ""),
        @("Lymphocytes", "31.7", "%", "20.0 - 45.0", ""),
        @("Mean corpuscular volume", "89.6", "fL", "80.0 - 100.0", ""),
        @("Red cell distribution width", "12.8", "%", "11.5 - 14.5", ""),
        @("Alkaline phosphatase", "63", "U/L", "40 - 130", ""),
        @("White blood cells", "5.46", "10^9/L", "4.00 - 10.00", ""),
        @("Apolipoprotein A1", "1.54", "g/L", "1.04 - 2.02", ""),
        @("Apolipoprotein B", "0.84", "g/L", "0.46 - 1.42", ""),
        @("Tiny footnote OCR stress", "Readable at 6.2 pt", "", "1234567890 abcdefghijk", "")
    )

    $bottom = $top - ($rows.Count * $rowHeight)
    foreach ($x in $cols) {
        Add-PdfLine $content $x $top $x $bottom
    }
    for ($i = 0; $i -le $rows.Count; $i++) {
        $y = $top - ($i * $rowHeight)
        Add-PdfLine $content $left $y 553 $y
    }

    for ($i = 0; $i -lt $rows.Count; $i++) {
        $row = $rows[$i]
        $fontSize = if ($i -eq 0) { 7.4 } elseif ($i -eq ($rows.Count - 1)) { 6.2 } else { 6.8 }
        $textY = $top - (($i + 1) * $rowHeight) + 5
        Add-PdfText $content ($cols[0] + 5) $textY $fontSize $row[0]
        Add-PdfText $content ($cols[1] + 5) $textY $fontSize $row[1]
        Add-PdfText $content ($cols[2] + 5) $textY $fontSize $row[2]
        Add-PdfText $content ($cols[3] + 5) $textY $fontSize $row[3]
        Add-PdfText $content ($cols[4] + 5) $textY $fontSize $row[4]
    }

    Add-PdfText $content 48 430 6.2 "Small text line: The quick brown fox jumps over 13 lazy dogs. 0O1Il| .,;: reference check."
    Add-PdfText $content 48 417 6.2 "Dense values: 4.32 5.10 12.80 63 0.38 31.7 89.6 1.54 0.84 2026-05-19."

    $contentText = $content.ToString()
    $encoding = [System.Text.Encoding]::ASCII
    $contentLength = $encoding.GetByteCount($contentText)
    $objects = [System.Collections.Generic.List[string]]::new()
    $objects.Add("1 0 obj`n<< /Type /Catalog /Pages 2 0 R >>`nendobj`n")
    $objects.Add("2 0 obj`n<< /Type /Pages /Kids [3 0 R] /Count 1 >>`nendobj`n")
    $objects.Add("3 0 obj`n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>`nendobj`n")
    $objects.Add("4 0 obj`n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>`nendobj`n")
    $objects.Add("5 0 obj`n<< /Length $contentLength >>`nstream`n$contentText`nendstream`nendobj`n")

    $pdf = [System.Text.StringBuilder]::new()
    [void]$pdf.Append("%PDF-1.4`n")
    $offsets = [System.Collections.Generic.List[int]]::new()
    foreach ($obj in $objects) {
        $offsets.Add($encoding.GetByteCount($pdf.ToString()))
        [void]$pdf.Append($obj)
    }

    $xrefOffset = $encoding.GetByteCount($pdf.ToString())
    [void]$pdf.Append("xref`n")
    [void]$pdf.Append("0 6`n")
    [void]$pdf.Append("0000000000 65535 f `n")
    foreach ($offset in $offsets) {
        [void]$pdf.AppendLine(("{0:0000000000} 00000 n " -f $offset))
    }
    [void]$pdf.Append("trailer`n<< /Size 6 /Root 1 0 R >>`n")
    [void]$pdf.Append("startxref`n$xrefOffset`n%%EOF`n")

    [System.IO.File]::WriteAllText($Path, $pdf.ToString(), $encoding)
}

function Convert-ToProofWebp {
    param(
        [string]$InputPng,
        [string]$OutputWebp,
        [int]$TargetBytes
    )

    $attempts = @(
        @{ Max = 2560; Quality = 88 },
        @{ Max = 2560; Quality = 82 },
        @{ Max = 2560; Quality = 76 },
        @{ Max = 2202; Quality = 72 },
        @{ Max = 2048; Quality = 72 }
    )

    $selected = $null
    foreach ($attempt in $attempts) {
        $tmp = "$OutputWebp.tmp.webp"
        Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
        & $script:MagickPath "$InputPng" -auto-orient -background white -alpha remove -alpha off -resize "$($attempt.Max)x$($attempt.Max)>" -define webp:lossless=false -quality $attempt.Quality "$tmp"
        if ($LASTEXITCODE -ne 0) {
            throw "ImageMagick failed while optimizing proof image."
        }

        Move-Item -LiteralPath $tmp -Destination $OutputWebp -Force
        $bytes = (Get-Item -LiteralPath $OutputWebp).Length
        $selected = [pscustomobject]@{
            Quality = $attempt.Quality
            MaxDimension = $attempt.Max
            Bytes = $bytes
        }

        if ($bytes -le $TargetBytes) {
            return $selected
        }
    }

    return $selected
}

function Get-MagickFx {
    param(
        [string]$ImagePath,
        [string]$Arguments
    )

    $output = Invoke-Expression "& `"$script:MagickPath`" `"$ImagePath`" $Arguments"
    if ($LASTEXITCODE -ne 0) {
        throw "ImageMagick metric command failed: $Arguments"
    }

    return [double]::Parse($output, [System.Globalization.CultureInfo]::InvariantCulture)
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $repoRoot "proof-quality-regression-output"
} elseif (-not [System.IO.Path]::IsPathRooted($OutputDir)) {
    $OutputDir = Join-Path $repoRoot $OutputDir
}

$pdfToPpm = Resolve-Tool "pdftoppm"
$script:MagickPath = Resolve-Tool "magick"

if (Test-Path -LiteralPath $OutputDir) {
    $resolvedOutput = (Resolve-Path -LiteralPath $OutputDir).Path
    if (-not $resolvedOutput.StartsWith($repoRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove output folder outside the repository: $resolvedOutput"
    }
    Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$intermediateDir = Join-Path $OutputDir "intermediate"
$finalDir = Join-Path $OutputDir "final"
New-Item -ItemType Directory -Force -Path $intermediateDir, $finalDir | Out-Null

$fixturePdf = Join-Path $intermediateDir "synthetic-lab-report.pdf"
$renderPrefix = Join-Path $intermediateDir "synthetic-lab-report"
$renderedPng = Join-Path $intermediateDir "synthetic-lab-report-1.png"
$finalWebp = Join-Path $finalDir "synthetic-lab-report__page-01.webp"
$targetBytes = [int](1.5 * 1024 * 1024)

New-SyntheticLabReportPdf $fixturePdf
$previousErrorActionPreference = $ErrorActionPreference
$ErrorActionPreference = "Continue"
& $pdfToPpm -r 108 -png -aa yes -aaVector yes -- "$fixturePdf" "$renderPrefix" 2>$null | Out-Null
$pdfToPpmExitCode = $LASTEXITCODE
$ErrorActionPreference = $previousErrorActionPreference
if ($pdfToPpmExitCode -ne 0) {
    throw "pdftoppm failed while rendering the synthetic proof PDF."
}

$pages = Get-ChildItem -LiteralPath $intermediateDir -Filter "synthetic-lab-report-*.png"
if ($pages.Count -ne 1) {
    throw "Expected exactly 1 rendered page, found $($pages.Count)."
}

$selected = Convert-ToProofWebp $renderedPng $finalWebp $targetBytes
$dimensionsRaw = & $script:MagickPath identify -format "%w %h" "$finalWebp"
if ($LASTEXITCODE -ne 0) {
    throw "ImageMagick identify failed for final proof image."
}

$parts = $dimensionsRaw -split "\s+"
$width = [int]$parts[0]
$height = [int]$parts[1]
$finalBytes = (Get-Item -LiteralPath $finalWebp).Length
$previousErrorActionPreference = $ErrorActionPreference
$ErrorActionPreference = "Continue"
$rmseRaw = & $script:MagickPath compare -metric RMSE "$renderedPng" "$finalWebp" "null:" 2>&1 | Out-String
$compareExitCode = $LASTEXITCODE
$ErrorActionPreference = $previousErrorActionPreference
if ($compareExitCode -gt 1) {
    throw "ImageMagick compare failed while checking proof quality."
}
$rmseNormalized = [double]::Parse((([regex]::Match($rmseRaw, "\((?<value>[0-9.]+)\)")).Groups["value"].Value), [System.Globalization.CultureInfo]::InvariantCulture)
$tinyTextInk = Get-MagickFx $finalWebp "-crop 760x48+70+580 -colorspace Gray -threshold 72% -format `"%[fx:1-mean]`" info:"
$tableInk = Get-MagickFx $finalWebp "-crop 760x300+60+155 -colorspace Gray -threshold 72% -format `"%[fx:1-mean]`" info:"

$failures = [System.Collections.Generic.List[string]]::new()
if ($width -lt 890 -or $height -lt 1260) {
    $failures.Add("Final proof dimensions are too small: ${width}x${height}.")
}
if ($finalBytes -gt $targetBytes) {
    $failures.Add("Final proof image exceeds target size: $finalBytes bytes.")
}
if ($finalBytes -lt 30000) {
    $failures.Add("Final proof image is suspiciously small: $finalBytes bytes.")
}
if ($selected.Quality -lt 88 -or $selected.MaxDimension -lt 2560) {
    $failures.Add("Synthetic fixture required fallback compression: quality=$($selected.Quality), maxDimension=$($selected.MaxDimension).")
}
if ($rmseNormalized -gt 0.04) {
    $failures.Add("WebP output drifted too far from rendered PNG: normalized RMSE=$rmseNormalized.")
}
if ($tinyTextInk -lt 0.006) {
    $failures.Add("Tiny-text crop has too little dark-pixel signal: $tinyTextInk.")
}
if ($tableInk -lt 0.015) {
    $failures.Add("Table crop has too little dark-pixel signal: $tableInk.")
}

$manifest = [pscustomobject]@{
    SourcePdf = [System.IO.Path]::GetFileName($fixturePdf)
    FinalFile = [System.IO.Path]::GetFileName($finalWebp)
    Pages = $pages.Count
    Width = $width
    Height = $height
    Bytes = $finalBytes
    KB = [math]::Round($finalBytes / 1KB, 1)
    Quality = $selected.Quality
    MaxDimension = $selected.MaxDimension
    TargetBytes = $targetBytes
    NormalizedRmse = $rmseNormalized
    TinyTextInk = $tinyTextInk
    TableInk = $tableInk
}

$manifest | ConvertTo-Json | Set-Content -Encoding ASCII -Path (Join-Path $OutputDir "manifest.json")

if (-not $KeepIntermediate) {
    Remove-Item -LiteralPath $intermediateDir -Recurse -Force
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ }
    throw "Proof quality regression failed with $($failures.Count) issue(s)."
}

Write-Host "Proof quality regression passed."
Write-Host "Output: $OutputDir"
Write-Host "Final image: $finalWebp"
Write-Host ("Dimensions: {0}x{1}, size: {2} KB, RMSE: {3}, tiny-text ink: {4}, table ink: {5}" -f $width, $height, ([math]::Round($finalBytes / 1KB, 1)), $rmseNormalized, $tinyTextInk, $tableInk)
