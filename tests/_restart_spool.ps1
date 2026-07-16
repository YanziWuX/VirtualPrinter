
    Remove-Item "C:\Windows\System32\mfilemon.log" -Force -ErrorAction SilentlyContinue
    & net stop spooler /y 2>&1 | Out-Null
    Start-Sleep 3
    & net start spooler 2>&1 | Out-Null
    Start-Sleep 8
    Get-Content "C:\Windows\System32\mfilemon.log"

