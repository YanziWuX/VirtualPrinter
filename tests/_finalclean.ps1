
    & net stop spooler /y 2>&1 | Out-Null
    Start-Sleep 3
    Remove-Item "C:\Windows\System32\mfilemon.dll" -Force -ErrorAction SilentlyContinue
    Remove-Item "C:\Windows\System32\mfilemon.log" -Force -ErrorAction SilentlyContinue
    & net start spooler 2>&1 | Out-Null
    "OK"

