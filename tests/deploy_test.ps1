param(
    [switch]$Cleanup
)

$ErrorActionPreference = "Stop"

$testPipeline = "D:\Code\YanziWu VirtualPrinter\tests\TestPipeline\TestPipeline\bin\Release\net9.0-windows\TestPipeline.exe"
$mfilemonDll = "D:\Code\YanziWu VirtualPrinter\tests\mfilemon.dll"
$mfilemonUI = "D:\Code\YanziWu VirtualPrinter\tests\mfilemonUI.dll"
$printerName = "YanziWu VirtualPrinter (V3 Test)"
$portName = "GSPDF:"
$driverName = "Microsoft PostScript Printer Driver"
$gsPath = "C:\Users\Administrator.DESKTOP-SIP3E3G\Downloads\gs10060\gs10060w64\bin\gswin64c.exe"
$outputDir = "D:\Code\YanziWu VirtualPrinter\test_output"

# 确保输出目录
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

if ($Cleanup) {
    Write-Host "=== 清理 ==="
    # 删除打印机
    Get-Printer -Name $printerName -ErrorAction SilentlyContinue | Remove-Printer -ErrorAction SilentlyContinue
    # 删除端口注册表
    $monKey = "HKLM:\SYSTEM\CurrentControlSet\Control\Print\Monitors\Multi File Port Monitor"
    Remove-Item -Path "$monKey\GSPDF:" -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -Path $monKey -Recurse -Force -ErrorAction SilentlyContinue
    # 删除 DLL
    Remove-Item "C:\Windows\System32\mfilemon.dll" -Force -ErrorAction SilentlyContinue
    Remove-Item "C:\Windows\System32\mfilemonUI.dll" -Force -ErrorAction SilentlyContinue
    Write-Host "清理完成"
    return
}

Write-Host "=== 1. 部署 MFileMon 端口监视器 ==="
Copy-Item $mfilemonDll "C:\Windows\System32\mfilemon.dll" -Force
Copy-Item $mfilemonUI "C:\Windows\System32\mfilemonUI.dll" -Force

$monKey = "HKLM:\SYSTEM\CurrentControlSet\Control\Print\Monitors\Multi File Port Monitor"
New-Item -Path $monKey -Force | Out-Null
New-ItemProperty -Path $monKey -Name "Driver" -Value "mfilemon.dll" -PropertyType String -Force | Out-Null

Write-Host "=== 2. 创建 GSPDF: 端口 ==="
$portKey = "$monKey\$portName"
New-Item -Path $portKey -Force | Out-Null
New-ItemProperty -Path $portKey -Name "OutputPath" -Value $outputDir -PropertyType String -Force | Out-Null
New-ItemProperty -Path $portKey -Name "FilePattern" -Value "output_%Y%M%D_%h%m%s_%i.pdf" -PropertyType String -Force | Out-Null
New-ItemProperty -Path $portKey -Name "UserCommand" -Value "`"$testPipeline`"" -PropertyType String -Force | Out-Null
New-ItemProperty -Path $portKey -Name "PipeData" -Value 1 -PropertyType DWord -Force | Out-Null
New-ItemProperty -Path $portKey -Name "WaitTermination" -Value 1 -PropertyType DWord -Force | Out-Null
New-ItemProperty -Path $portKey -Name "WaitTimeout" -Value 120 -PropertyType DWord -Force | Out-Null
New-ItemProperty -Path $portKey -Name "HideProcess" -Value 0 -PropertyType DWord -Force | Out-Null
New-ItemProperty -Path $portKey -Name "LogLevel" -Value 2 -PropertyType DWord -Force | Out-Null

Write-Host "=== 3. 重启 Spooler ==="
Remove-Item "C:\Windows\System32\mfilemon.log" -Force -ErrorAction SilentlyContinue
Restart-Service -Name Spooler -Force -ErrorAction SilentlyContinue
if (-not $?) {
    & net stop spooler /y 2>&1 | Out-Null
    Start-Sleep 3
    & net start spooler 2>&1 | Out-Null
}
Start-Sleep 5

Write-Host "=== 4. 确认端口加载 ==="
Get-Content "C:\Windows\System32\mfilemon.log"
$port = Get-PrinterPort -Name $portName -ErrorAction SilentlyContinue
if (-not $port) {
    Write-Warning "端口未加载到 Spooler"
}

Write-Host "=== 5. 确保 V3 PostScript 驱动 ==="
$exists = Get-PrinterDriver -Name $driverName -ErrorAction SilentlyContinue
if (-not $exists) {
    Write-Host "V3 PostScript 驱动未安装，需要手动注册..."
    # 尝试通过 printui 安装
    & rundll32 printui.dll,PrintUIEntry /ia /m "$driverName" /f "C:\Windows\inf\ntprint.inf" 2>&1 | Out-Null
    Start-Sleep 3
    $exists = Get-PrinterDriver -Name $driverName -ErrorAction SilentlyContinue
}
if ($exists) {
    Write-Host "V3 驱动已就绪: $($exists.Name) v$($exists.MajorVersion)"
    
    Write-Host "=== 6. 创建打印机 ==="
    Get-Printer -Name $printerName -ErrorAction SilentlyContinue | Remove-Printer -ErrorAction SilentlyContinue
    
    Add-Printer -Name $printerName -DriverName $driverName -PortName $portName -ErrorAction SilentlyContinue
    $printer = Get-Printer -Name $printerName -ErrorAction SilentlyContinue
    if ($printer) {
        Write-Host "打印机已创建: $($printer.Name)"
        Write-Host "  端口: $($printer.PortName)"
        Write-Host "  驱动: $($printer.DriverName)"
        Write-Host ""
        Write-Host "=== 部署完成! ==="
        Write-Host "请打印文档到 '$printerName'"
        Write-Host "TestPipeline 路径: $testPipeline"
    } else {
        Write-Warning "打印机创建失败"
    }
} else {
    Write-Warning "V3 PostScript 驱动不可用，请先安装"
}
