param(
    [string]$WatchDir = "C:\WINDOWS\TEMP\VirtualPrinter",
    [string]$TestPipeline = "D:\Code\YanziWu VirtualPrinter\tests\TestPipeline\TestPipeline\bin\Release\net9.0-windows\TestPipeline.exe",
    [string]$OutputDir = "C:\Temp\VPPrint",
    [int]$PollIntervalMs = 1000
)

Write-Host "=== VPPostScriptMon Poller ===" -ForegroundColor Cyan
Write-Host "Watching: $WatchDir"
Write-Host "Pipeline: $TestPipeline"
Write-Host "Output:   $OutputDir"
Write-Host ""

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

$processed = @{}
$pollInterval = [TimeSpan]::FromMilliseconds($PollIntervalMs)

while ($true) {
    if (Test-Path $WatchDir) {
        $files = Get-ChildItem $WatchDir -Filter "*.tmp" | Sort-Object LastWriteTime
        foreach ($f in $files) {
            if ($processed.ContainsKey($f.FullName)) { continue }
            if ($f.Length -eq 0) { continue }

            # 等待文件写入完成 (500ms 稳定检测)
            $size = $f.Length
            Start-Sleep -Milliseconds 500
            if (-not (Test-Path $f.FullName)) { continue }
            $newSize = (Get-Item $f.FullName).Length
            if ($newSize -ne $size) { continue }

            $timestamp = Get-Date -Format "HH:mm:ss.fff"
            Write-Host "[$timestamp] New job: $($f.Name) ($($f.Length) bytes)" -ForegroundColor Green

            $outputFile = Join-Path $OutputDir "vpmon_$($f.BaseName).pdf"
            & $TestPipeline $f.FullName --output $outputFile

            if ($LASTEXITCODE -eq 0 -and (Test-Path $outputFile)) {
                $outSize = (Get-Item $outputFile).Length
                Write-Host "  -> PDF saved: $outputFile ($outSize bytes)" -ForegroundColor Green
            } else {
                Write-Host "  -> Conversion FAILED (exit=$LASTEXITCODE)" -ForegroundColor Red
            }

            $processed[$f.FullName] = $true
        }
    }
    Start-Sleep -Milliseconds $PollIntervalMs
}
