
    Write-Host "Step 1: Copy DLLs"
    Copy-Item "D:\Code\YanziWu VirtualPrinter\tests\mfilemon.dll" "C:\Windows\System32\mfilemon.dll" -Force
    Copy-Item "D:\Code\YanziWu VirtualPrinter\tests\mfilemonUI.dll" "C:\Windows\System32\mfilemonUI.dll" -Force
    Write-Host "  DLLs copied"
    
    Write-Host "Step 2: Register monitor"
    $monKey = "HKLM:\SYSTEM\CurrentControlSet\Control\Print\Monitors\Multi File Port Monitor"
    New-Item -Path $monKey -Force | Out-Null
    New-ItemProperty -Path $monKey -Name "Driver" -Value "mfilemon.dll" -PropertyType String -Force | Out-Null
    
    $portKey = "$monKey\GSPDF:"
    New-Item -Path $portKey -Force | Out-Null
    $tp = "D:\Code\YanziWu VirtualPrinter\tests\TestPipeline\TestPipeline\bin\Release\net9.0-windows\TestPipeline.exe"
    New-ItemProperty -Path $portKey -Name "OutputPath" -Value "D:\Code\YanziWu VirtualPrinter\test_output" -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $portKey -Name "FilePattern" -Value "output_%Y%M%D_%h%m%s_%i.pdf" -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $portKey -Name "UserCommand" -Value "`"$tp`"" -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $portKey -Name "PipeData" -Value 1 -PropertyType DWord -Force | Out-Null
    New-ItemProperty -Path $portKey -Name "WaitTermination" -Value 1 -PropertyType DWord -Force | Out-Null
    New-ItemProperty -Path $portKey -Name "WaitTimeout" -Value 120 -PropertyType DWord -Force | Out-Null
    New-ItemProperty -Path $portKey -Name "HideProcess" -Value 0 -PropertyType DWord -Force | Out-Null
    New-ItemProperty -Path $portKey -Name "LogLevel" -Value 2 -PropertyType DWord -Force | Out-Null
    Write-Host "  Registry configured"
    
    Write-Host "Step 3: Restart spooler"
    Remove-Item "C:\Windows\System32\mfilemon.log" -Force -ErrorAction SilentlyContinue
    & net stop spooler /y 2>&1 | Out-Null
    Start-Sleep 3
    & net start spooler 2>&1 | Out-Null
    Start-Sleep 5
    Write-Host "  Spooler restarted"
    
    Write-Host "Step 4: Check MFileMon log"
    if (Test-Path "C:\Windows\System32\mfilemon.log") {
        Get-Content "C:\Windows\System32\mfilemon.log"
    } else {
        Write-Host "  NO LOG FILE"
    }
    
    Write-Host "Step 5: Check port"
    $port = Get-PrinterPort -Name "GSPDF:" -ErrorAction SilentlyContinue
    if ($port) { Write-Host "  Port GSPDF: found" } else { Write-Host "  Port NOT found" }
    
    Write-Host "Step 6: Check V3 driver"
    $drv = Get-PrinterDriver -Name "Microsoft PostScript Printer Driver" -ErrorAction SilentlyContinue
    if ($drv) { Write-Host "  V3 driver: $($drv.Name) v$($drv.MajorVersion)" } else { Write-Host "  V3 driver NOT found" }
    
    Write-Host "Step 7: Create printer"
    $printerName = "YanziWu VirtualPrinter (V3 Test)"
    Get-Printer -Name $printerName -ErrorAction SilentlyContinue | Remove-Printer -ErrorAction SilentlyContinue
    Start-Sleep 2
    Add-Printer -Name $printerName -DriverName "Microsoft PostScript Printer Driver" -PortName "GSPDF:" -ErrorAction Stop
    $printer = Get-Printer -Name $printerName -ErrorAction SilentlyContinue
    if ($printer) {
        Write-Host "  Printer: $($printer.Name)"
        Write-Host "  Port: $($printer.PortName)"
        Write-Host "  Driver: $($printer.DriverName)"
    } else {
        Write-Host "  Printer creation FAILED"
    }
    
    Write-Host "DONE"

