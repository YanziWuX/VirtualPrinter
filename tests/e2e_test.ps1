param(
    [ValidateSet("pdf","png","jpg","all")]
    [string]$Format = "all",
    [string]$PrinterName = "YanziWu PDF-IMG Printer",
    [string]$OutputDir = "$env:TEMP\e2e_vp_output",
    [string]$WaitDir = "$env:WINDIR\TEMP\VirtualPrinter",
    [string]$TestPipeline = "D:\Code\YanziWu VirtualPrinter\tests\TestPipeline\TestPipeline\bin\Release\net9.0-windows\TestPipeline.exe"
)

$ErrorActionPreference = "Stop"

# 1. Verify prerequisites
if (-not (Get-Printer -Name $PrinterName -ErrorAction SilentlyContinue)) {
    Write-Host "ERROR: Printer '$PrinterName' not found" -ForegroundColor Red
    exit 1
}
if (-not (Test-Path $TestPipeline)) {
    Write-Host "ERROR: TestPipeline.exe not found at $TestPipeline" -ForegroundColor Red
    exit 1
}
$gs = "C:\Users\Administrator.DESKTOP-SIP3E3G\Downloads\gs10060\gs10060w64\bin\gswin64c.exe"
if (-not (Test-Path $gs)) {
    Write-Host "ERROR: Ghostscript not found at $gs" -ForegroundColor Red
    exit 1
}

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

# 2. Clean previous output
Get-ChildItem $WaitDir -Filter "*.tmp" | Remove-Item -Force -ErrorAction SilentlyContinue
Get-ChildItem $OutputDir -Filter "e2e_test*" | Remove-Item -Force -ErrorAction SilentlyContinue

# 3. Generate test content
$testFile = "$env:TEMP\e2e_test_page.txt"
@"
========================================
     VirtualPrinter End-to-End Test
     $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
========================================

This is a test page for the VirtualPrinter pipeline.

ABCDEFGHIJKLMNOPQRSTUVWXYZ
abcdefghijklmnopqrstuvwxyz
0123456789 !@#`$%^&*()_+-=[]{}|;:',.<>?/~

The quick brown fox jumps over the lazy dog.
Pack my box with five dozen liquor jugs.

========================================
"@ | Out-File -Encoding ASCII $testFile

# 4. Print
Write-Host "`n[1/4] Printing test page..." -ForegroundColor Cyan
$psi = @{
    FilePath = "notepad.exe"
    ArgumentList = "/PT", "`"$testFile`"", "`"$PrinterName`""
    Wait = $true
    NoNewWindow = $true
}
Start-Process @psi
Write-Host "       Print command sent." -ForegroundColor Green

# 5. Wait for VP_Port capture file (*.tmp in $WaitDir)
Write-Host "[2/4] Waiting for capture file in $WaitDir..." -ForegroundColor Cyan
$timeout = 30
$elapsed = 0
$captureFile = $null
while ($elapsed -lt $timeout) {
    $files = Get-ChildItem $WaitDir -Filter "*.tmp" -ErrorAction SilentlyContinue
    if ($files) {
        $captureFile = $files | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        break
    }
    Start-Sleep -Seconds 1
    $elapsed++
}
if (-not $captureFile) {
    Write-Host "ERROR: No capture file created within ${timeout}s" -ForegroundColor Red
    exit 1
}
$psSize = $captureFile.Length
Write-Host "       Capture file: $($captureFile.FullName) ($psSize bytes)" -ForegroundColor Green

# 6. Verify it's PostScript (PJL wrapped)
$header = [System.Text.Encoding]::ASCII.GetString(
    [System.IO.File]::ReadAllBytes($captureFile.FullName), 0, 80)
if ($header -notmatch '%!PS-Adobe|EPSF|PJL') {
    Write-Host "WARNING: capture file does not appear to be PostScript" -ForegroundColor Yellow
    Write-Host "  Header: $($header -replace "`n"," ")"
} else {
    Write-Host "       Verified: PostScript data" -ForegroundColor Green
}

# 7. Convert to selected formats
$formats = if ($Format -eq "all") { @("pdf","png","jpg") } else { @($Format) }
$results = @{}

foreach ($fmt in $formats) {
    Write-Host "[3/4] Converting to $fmt..." -ForegroundColor Cyan
    $outFile = Join-Path $OutputDir "e2e_test.$fmt"
    & $TestPipeline $captureFile.FullName --output $outFile
    if ($LASTEXITCODE -eq 0 -and (Test-Path $outFile)) {
        $size = (Get-Item $outFile).Length
        Write-Host "       $fmt -> $outFile ($size bytes)" -ForegroundColor Green
        $results[$fmt] = "OK ($size bytes)"
    } else {
        Write-Host "       $fmt -> FAILED (exit=$LASTEXITCODE)" -ForegroundColor Red
        $results[$fmt] = "FAILED"
    }
}

# 8. Summary
Write-Host "`n[4/4] Results" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Printer:    $PrinterName"
Write-Host "  Driver:     $((Get-Printer -Name $PrinterName).DriverName)"
Write-Host "  Port:       $((Get-Printer -Name $PrinterName).PortName)"
Write-Host "  Capture:    $psSize bytes PostScript"
Write-Host "  Converter:  $TestPipeline"
Write-Host "  Ghostscript: $gs"
Write-Host "  Output Dir: $OutputDir"
foreach ($kv in $results.GetEnumerator()) {
    $color = if ($kv.Value -like "OK*") { "Green" } else { "Red" }
    Write-Host "  $($kv.Key): " -NoNewline
    Write-Host $kv.Value -ForegroundColor $color
}
Write-Host "========================================`n" -ForegroundColor Cyan

# Cleanup
Remove-Item $testFile -Force -ErrorAction SilentlyContinue
