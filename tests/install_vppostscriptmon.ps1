param(
    [string]$DllSource = "D:\Code\YanziWu VirtualPrinter\tests\VPPostScriptMon_build\Release\VPPostScriptMon.dll",
    [string]$MonitorName = "VirtualPrinter Port Monitor",
    [string]$PortName = "VP_Port"
)

$ErrorActionPreference = "Stop"

# 1. Copy DLL to system32
$dllDest = Join-Path $env:SystemRoot "System32\VPPostScriptMon.dll"
Write-Host "[1/5] Copying DLL..." -ForegroundColor Cyan
Write-Host "       $DllSource -> $dllDest"
Copy-Item $DllSource $dllDest -Force
Write-Host "       OK ($((Get-Item $dllDest).Length) bytes)" -ForegroundColor Green

# 2. Create monitor registry key
$monRegKey = "HKLM:\SYSTEM\CurrentControlSet\Control\Print\Monitors\$MonitorName"
Write-Host "[2/5] Creating registry key: $monRegKey" -ForegroundColor Cyan
if (-not (Test-Path $monRegKey)) {
    New-Item -Path $monRegKey -Force | Out-Null
}
Set-ItemProperty -Path $monRegKey -Name "Driver" -Value "VPPostScriptMon.dll" -Type String
Write-Host "       OK" -ForegroundColor Green

# 3. Restart spooler to load the monitor
Write-Host "[3/5] Restarting spooler..." -ForegroundColor Cyan
Restart-Service -Name Spooler -Force
Write-Host "       OK" -ForegroundColor Green

# 4. Verify monitor loaded
Write-Host "[4/5] Verifying monitor..." -ForegroundColor Cyan
$monitors = Get-Printer -ErrorAction SilentlyContinue | Select-Object -First 1
$loaded = Get-ChildItem "HKLM:\SYSTEM\CurrentControlSet\Control\Print\Monitors" -ErrorAction SilentlyContinue
$found = $loaded | Where-Object { $_.PSChildName -eq $MonitorName }
if ($found) {
    Write-Host "       Monitor '$MonitorName' found in registry" -ForegroundColor Green
} else {
    Write-Host "       WARNING: Monitor not found (may need manual check)" -ForegroundColor Yellow
}

# 5. Create port using Add-PrinterPort
Write-Host "[5/5] Creating port '$PortName'..." -ForegroundColor Cyan
$existing = Get-PrinterPort -Name "$PortName*" -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "       Port already exists: $($existing.Name)" -ForegroundColor Yellow
} else {
    try {
        Add-PrinterPort -Name $PortName
        Write-Host "       Port '$PortName' created" -ForegroundColor Green
    }
    catch {
        Write-Host "       Add-PrinterPort failed: $_" -ForegroundColor Yellow
        Write-Host "       Trying XcvData AddPort..." -ForegroundColor Yellow
        
        # Try via C# XcvData call
        $code = @'
using System;
using System.Runtime.InteropServices;

public class XcvPort {
    [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool XcvData(string hXcv, string pszDataName, IntPtr pInputData, uint cbInputData, IntPtr pOutputData, uint cbOutputData, out uint pcbOutputNeeded, out uint pdwStatus);

    public static void AddPort(string monitor, string port) {
        string xcv = $",XcvPort {monitor}";
        uint needed, status;
        IntPtr input = Marshal.StringToCoTaskMemUni(port);
        bool ok = XcvData(xcv, "AddPort", input, (uint)((port.Length + 1) * 2), IntPtr.Zero, 0, out needed, out status);
        Marshal.FreeCoTaskMem(input);
        Console.WriteLine($"XcvData AddPort: ok={ok}, status={status}");
    }
}
'@
        Add-Type -TypeDefinition $code -Language CSharp
        [XcvPort]::AddPort($MonitorName, $PortName)
    }
}

Write-Host "`n=== Summary ===" -ForegroundColor Cyan
Write-Host "DLL:     $dllDest"
Write-Host "Monitor: $MonitorName"
Write-Host "Port:    $PortName"
Write-Host "`nNext: Install V3 PS driver on port $PortName"
