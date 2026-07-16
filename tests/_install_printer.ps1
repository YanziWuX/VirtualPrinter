
    # 安装 Canon Generic Plus PS3 V3 驱动
    $canonInf = "$PSScriptRoot\..\drivers\CanonGenericPlusPS3\CNS30MA64.INF"
    $driverName = "Canon Generic Plus PS3"
    $printerName = "YanziWu PDF-IMG Printer"
    $portName = "VP_Port"

    $exists = Get-PrinterDriver -Name $driverName -ErrorAction SilentlyContinue
    if (-not $exists) {
        "正在安装 Canon 驱动..."
        pnputil /add-driver "$canonInf"
        Start-Sleep 3
        $exists = Get-PrinterDriver -Name $driverName -ErrorAction SilentlyContinue
        if (-not $exists) {
            "错误: Canon 驱动安装失败"
            exit 1
        }
    }

    # 确保 VP_Port 存在
    $port = Get-PrinterPort -Name $portName -ErrorAction SilentlyContinue
    if (-not $port) {
        "正在创建 VP_Port..."
        $monKey = 'HKLM:\SYSTEM\CurrentControlSet\Control\Print\Monitors\VirtualPrinter Port Monitor'
        $portsKey = "$monKey\Ports"
        if (-not (Test-Path $monKey)) { New-Item -Path $monKey -Force | Out-Null }
        if (-not (Test-Path $portsKey)) { New-Item -Path $portsKey -Force | Out-Null }
        New-ItemProperty -Path $portsKey -Name $portName -Value '' -PropertyType String -Force | Out-Null
        Restart-Service -Name Spooler -Force
        Start-Sleep 3
    }

    # 移除旧打印机
    Get-Printer -Name $printerName -ErrorAction SilentlyContinue | Remove-Printer -ErrorAction SilentlyContinue

    # 创建打印机
    $result = Add-Printer -Name $printerName -DriverName $driverName -PortName $portName -ErrorAction SilentlyContinue 2>&1
    if (-not $result) {
        # WMI 备用方案
        $wmi = ([WMIClass]"Win32_Printer").CreateInstance()
        $wmi.DriverName = $driverName
        $wmi.PortName = $portName
        $wmi.DeviceID = $printerName
        $wmi.Put() | Out-Null
    }

    # 验证
    $printer = Get-Printer -Name $printerName -ErrorAction SilentlyContinue
    if ($printer) {
        "打印机已创建: $($printer.Name), 端口: $($printer.PortName), 驱动: $($printer.DriverName)"
    } else {
        "打印机创建失败"
    }
