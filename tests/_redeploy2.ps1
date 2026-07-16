
    # ????????????? (??????XcvData AddPort ???)
    $monKey = "HKLM:\SYSTEM\CurrentControlSet\Control\Print\Monitors\Multi File Port Monitor"
    $portKey = "$monKey\GSPDF:"
    if (-not (Test-Path $portKey)) {
        "????????????..."
        New-Item -Path $portKey -Force | Out-Null
        New-ItemProperty -Path $portKey -Name "OutputPath" -Value "D:\Code\YanziWu VirtualPrinter\test_output" -PropertyType String -Force | Out-Null
        New-ItemProperty -Path $portKey -Name "FilePattern" -Value "output_%Y%M%D_%h%m%s_%i.pdf" -PropertyType String -Force | Out-Null
        $tp = "D:\Code\YanziWu VirtualPrinter\tests\TestPipeline\TestPipeline\bin\Release\net9.0-windows\TestPipeline.exe"
        New-ItemProperty -Path $portKey -Name "UserCommand" -Value "`"$tp`"" -PropertyType String -Force | Out-Null
        New-ItemProperty -Path $portKey -Name "PipeData" -Value 1 -PropertyType DWord -Force | Out-Null
        New-ItemProperty -Path $portKey -Name "WaitTermination" -Value 1 -PropertyType DWord -Force | Out-Null
        New-ItemProperty -Path $portKey -Name "WaitTimeout" -Value 120 -PropertyType DWord -Force | Out-Null
        New-ItemProperty -Path $portKey -Name "HideProcess" -Value 0 -PropertyType DWord -Force | Out-Null
        New-ItemProperty -Path $portKey -Name "LogLevel" -Value 2 -PropertyType DWord -Force | Out-Null
        "????????"
    } else {
        "????????"
    }
    
    # ?? spooler
    & net stop spooler /y 2>&1 | Out-Null
    Start-Sleep 3
    Remove-Item "C:\Windows\System32\mfilemon.log" -Force -ErrorAction SilentlyContinue
    & net start spooler 2>&1 | Out-Null
    Start-Sleep 8
    
    # ????
    if (Test-Path "C:\Windows\System32\mfilemon.log") {
        Get-Content "C:\Windows\System32\mfilemon.log"
    } else { "?????" }
    
    # ????
    $port = Get-PrinterPort -Name "GSPDF:" -ErrorAction SilentlyContinue
    if ($port) { "?????: $($port.Name)" } else { "?????" }

