
    $ErrorActionPreference = "Stop"
    
    Write-Host "=== 1. ???? ==="
    # ?? spooler
    & net stop spooler /y 2>&1 | Out-Null
    Start-Sleep 3
    
    # ?? DLL
    Remove-Item "C:\Windows\System32\mfilemon.dll" -Force -ErrorAction SilentlyContinue
    Remove-Item "C:\Windows\System32\mfilemonUI.dll" -Force -ErrorAction SilentlyContinue
    Remove-Item "C:\Windows\System32\mfilemon.log" -Force -ErrorAction SilentlyContinue
    
    # ????????
    Remove-Item "HKLM:\SYSTEM\CurrentControlSet\Control\Print\Monitors\Multi File Port Monitor" -Recurse -Force -ErrorAction SilentlyContinue
    
    # ?????
    $ps = Get-Printer -Name "YanziWu VirtualPrinter (V3 Test)" -ErrorAction SilentlyContinue
    if ($ps) { Remove-Printer -Name $ps.Name -ErrorAction SilentlyContinue }
    
    Write-Host "=== 2. ???? spooler ==="
    & net start spooler 2>&1 | Out-Null
    Start-Sleep 3
    
    Write-Host "=== 3. ?? DLL ==="
    Copy-Item "D:\Code\YanziWu VirtualPrinter\tests\mfilemon.dll" "C:\Windows\System32\mfilemon.dll" -Force
    Copy-Item "D:\Code\YanziWu VirtualPrinter\tests\mfilemonUI.dll" "C:\Windows\System32\mfilemonUI.dll" -Force
    
    Write-Host "=== 4. ????? ==="
    $monKey = "HKLM:\SYSTEM\CurrentControlSet\Control\Print\Monitors\Multi File Port Monitor"
    New-Item -Path $monKey -Force | Out-Null
    New-ItemProperty -Path $monKey -Name "Driver" -Value "mfilemon.dll" -PropertyType String -Force | Out-Null
    
    Write-Host "=== 5. ?? spooler ?? DLL ==="
    & net stop spooler /y 2>&1 | Out-Null
    Start-Sleep 3
    Remove-Item "C:\Windows\System32\mfilemon.log" -Force -ErrorAction SilentlyContinue
    & net start spooler 2>&1 | Out-Null
    Start-Sleep 8
    
    Write-Host "=== 6. ?????? ==="
    Get-Content "C:\Windows\System32\mfilemon.log"
    
    Write-Host "=== 7. ?? GSPDF: ????? ==="
    # ??? spooler ???????? DLL ???????
    & net stop spooler /y 2>&1 | Out-Null
    Start-Sleep 2
    
    $portKey = "$monKey\GSPDF:"
    New-Item -Path $portKey -Force | Out-Null
    New-ItemProperty -Path $portKey -Name "OutputPath" -Value "D:\Code\YanziWu VirtualPrinter\test_output" -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $portKey -Name "FilePattern" -Value "output_%Y%M%D_%h%m%s_%i.pdf" -PropertyType String -Force | Out-Null
    $tp = "D:\Code\YanziWu VirtualPrinter\tests\TestPipeline\TestPipeline\bin\Release\net9.0-windows\TestPipeline.exe"
    New-ItemProperty -Path $portKey -Name "UserCommand" -Value "`"$tp`"" -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $portKey -Name "PipeData" -Value 1 -PropertyType DWord -Force | Out-Null
    New-ItemProperty -Path $portKey -Name "WaitTermination" -Value 1 -PropertyType DWord -Force | Out-Null
    New-ItemProperty -Path $portKey -Name "WaitTimeout" -Value 120 -PropertyType DWord -Force | Out-Null
    New-ItemProperty -Path $portKey -Name "HideProcess" -Value 0 -PropertyType DWord -Force | Out-Null
    
    Write-Host "=== 8. ?? spooler ==="
    Remove-Item "C:\Windows\System32\mfilemon.log" -Force -ErrorAction SilentlyContinue
    & net start spooler 2>&1 | Out-Null
    Start-Sleep 8
    
    Write-Host "=== 9. ???? ==="
    Get-Content "C:\Windows\System32\mfilemon.log"
    
    Write-Host "=== 10. ???? ==="
    $port = Get-PrinterPort -Name "GSPDF:" -ErrorAction SilentlyContinue
    if ($port) { "?????: $($port.Name) ???: $($port.PortMonitor)" } else { "?????" }
    
    "=== ?? ==="

