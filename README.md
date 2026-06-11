# VirtualPrinter

Windows 虚拟打印机驱动程序 —— 拦截应用程序的打印作业，将 PostScript 数据转换为 PDF、PNG、JPEG、BMP、TIFF 格式。

不需要微软驱动签名认证，支持 Windows 7 SP1 / 10 / 11 (x64)。

## 项目结构

| 模块 | 语言 | 说明 |
|------|------|------|
| `VPPostScriptMon` | C++ | 端口监控 DLL，注入打印后台处理程序 |
| `VPGhostLib` | C# | Ghostscript 封装库 |
| `VirtualPrinterService` | C# | Windows 服务，管理打印作业队列 |
| `VirtualPrinterSaveDialog` | C# (WPF) | 保存格式/设置对话框 |
| `VirtualPrinterManager` | C# (WPF) | 打印机安装/卸载/管理工具 |
| `VirtualPrinterLauncher` | C# | 自解压启动器 |
| `EnvChecker` | C++ | 环境检测 DLL |

## 构建

需要 Visual Studio 2022 (C++ 工具集)、CMake 3.10+、.NET SDK 8.0+。

```
build.bat
```

## 安装

运行 `dist\VirtualPrinter_Setup.exe`（Inno Setup 打包）或 `dist\VirtualPrinterLauncher.exe`（便携版）。
