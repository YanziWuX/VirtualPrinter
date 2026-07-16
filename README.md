# VirtualPrinter

Windows 虚拟打印机驱动 —— 拦截应用程序的打印作业，将 PostScript 数据转换为 PDF、PNG、JPEG、BMP、TIFF 格式。

不需要微软驱动签名认证，支持 Windows 7 SP1 / 10 / 11 (x64)。

## 项目结构

| 模块 | 语言 | 目标框架 | 说明 |
|------|------|----------|------|
| `VPPostScriptMon` | C++ | Win32/x64 | 端口监视器 DLL（注入 spoolsv.exe），捕获 PS 数据 |
| `VPGhostLib` | C# | .NET Framework 4.8 | Ghostscript 封装库 |
| `VirtualPrinterService` | C# | .NET Framework 4.8 | Windows 服务，管理打印作业队列 |
| `VirtualPrinterSaveDialog` | C# (WPF) | .NET Framework 4.8 | 保存格式/设置对话框 |
| `VirtualPrinterManager` | C# (WPF) | .NET Framework 4.8 | 打印机安装/卸载/管理工具 |
| `VirtualPrinterLauncher` | C# | .NET Framework 4.8 | 自解压启动器（便携模式） |
| `EnvChecker` | C++ | Win32/x64 | 环境检测 DLL（Inno Setup 调用） |
| `RedmonRedirector` | C++ | Win32/x64 | Redmon 风格 stdin 重定向器（备用方案） |
| `VirtualPrinter.Tests` / `GsTest` | C# | .NET Framework 4.8 | 单元测试和 GS 管道集成测试 |

## 打印驱动

使用 **Canon Generic Plus PS3 (V3)** PostScript 打印机驱动，已随安装包分发。端口监视器基于注册表注册的 `VirtualPrinter Port Monitor`，端口名 `VP_Port`。

## 构建

需要 Visual Studio 2022 (C++ 工具集)、CMake 3.10+、.NET SDK（支持 net48 目标）、7-Zip。

```
build.bat
```

## 安装

运行 `dist\VirtualPrinter_Setup.exe`（Inno Setup 安装包），或通过 `VirtualPrinterLauncher.exe` 便携模式启动。

## 输出格式

PDF / PNG / JPEG / BMP / TIFF，支持 RGB、灰度、CMYK、黑白二值四种色彩模式，72–2400 DPI 可选，支持水印叠加。
