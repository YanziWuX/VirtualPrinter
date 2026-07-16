
    # 1. ??????
    New-Item -ItemType Directory -Path "C:\Temp\VPPrint" -Force | Out-Null
    New-Item -ItemType Directory -Path "C:\Temp\VPPrint\jobs" -Force | Out-Null
    New-Item -ItemType Directory -Path "$env:APPDATA\VirtualPrinter" -Force | Out-Null
    
    # 2. ????????
    sc.exe stop VirtualPrinterService 2>$null | Out-Null
    Start-Sleep 2
    sc.exe delete VirtualPrinterService 2>$null | Out-Null
    Start-Sleep 1
    
    # 3. ????
    $svcPath = "D:\Code\YanziWu VirtualPrinter\dist\bin\VirtualPrinterService.exe"
    sc.exe create VirtualPrinterService binPath="`"$svcPath`"" start=auto 2>&1 | Out-Null
    
    # 4. ?????? SID ??????
    $sid = (Get-WmiObject Win32_UserAccount | Where-Object { $_.Name -eq $env:USERNAME -and $_.Domain -eq $env:COMPUTERNAME }).SID
    if (-not $sid) {
        $sid = (whoami /user | Select-String -Pattern "S-\d-\d+-\d+").Matches.Value
    }
    
    # 5. ????
    sc.exe start VirtualPrinterService 2>&1
    
    # 6. ??????
    Start-Sleep 3
    $svc = Get-Service VirtualPrinterService -ErrorAction SilentlyContinue
    if ($svc -and $svc.Status -eq 'Running') {
        "?????"
    } else {
        "????: $($svc.Status)"
    }

