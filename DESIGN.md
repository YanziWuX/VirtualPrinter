# VirtualPrinter 详细设计文档

## 1. 项目概述

Windows 虚拟打印机驱动，将任何应用程序的打印任务输出为 PDF、PNG、JPEG、BMP、TIFF 格式。无需微软驱动签名认证，支持 Windows 7 SP1 / 10 / 11 (x64)，自动检测并补全所有运行环境。

## 2. 用户操作流程

### 2.1 安装打印机

```
① 用户运行 VirtualPrinter_Setup.exe
② 释放核心文件到安装目录 (Program Files\VirtualPrinter)
③ 检测系统环境
   ├── Windows 版本 / 架构 → 不兼容则阻止
   ├── .NET Framework → Win7 从 CDN 下载安装 4.8
   ├── VC++ Redist → Win10/11 从安装包释放安装; Win7 从 CDN 下载
   ├── Print Spooler → 未运行则自动启动
   └── PostScript 驱动 → 从安装包释放 Canon 驱动 INF
④ 注册端口监视器：复制 VPPostScriptMon.dll → system32，写入注册表 HKLM\...\Monitors\VirtualPrinter Port Monitor\
⑤ 添加打印机端口 "VP_Port"（注册表 HKLM\...\Monitors\...\Ports\VP_Port）
⑥ 安装打印机驱动 "Canon Generic Plus PS3"（从内嵌 INF/CAB 安装）
⑦ 创建打印机 "YanziWu PDF-IMG Printer"（绑定 VP_Port）
⑧ 安装并启动 VirtualPrinterService
⑨ 安装完成
```

### 2.2 卸载打印机

```
① 用户在设置 GUI 中点击 [卸载打印机]
② 界面显示两个选项 (默认勾选):
   ☑ 同时删除端口 (VP_Port)
   ☑ 同时删除打印机驱动
③ 用户点击 [确认卸载]
④ 停止 VirtualPrinterService (sc stop)
⑤ 删除打印机 "YanziWu PDF-IMG Printer"
⑥ 根据勾选状态:
   - 勾选 "删除端口" → 删除端口 "VP_Port"（Remove-PrinterPort）
   - 勾选 "删除驱动" → 删除驱动 "Canon Generic Plus PS3"
⑦ 删除 VirtualPrinterService (sc delete)
⑧ 删除 VPPostScriptMon.dll
⑨ 删除安装目录
⑩ 卸载完成
```

### 2.3 打印文档

```
① 用户在应用程序 (Word/Excel/浏览器) 中:
   文件 → 打印 → 选择 "VirtualPrinter" → 打印
② Windows Spooler → PostScript 驱动(Canon Generic Plus PS3) → 端口监视器捕获 PS 数据
③ 端口监视器写入临时文件 `%ProgramData%\VirtualPrinter\{JobId}.tmp` → 通知后台服务
④ 后台服务启动 VirtualPrinterSaveDialog (用户桌面)
⑤ 弹出保存对话框 (模态置顶):
   ├── 文档名称 (自动填充, 可编辑)
   ├── 输出格式: PDF / PNG / JPEG / BMP / TIFF
   ├── 颜色模式: RGB / 灰度 / CMYK / 黑白二值
   ├── 分辨率: 72-2400 DPI
   ├── 多页处理: PDF 合并 / 图片分页 或 多页 TIFF
   ├── 水印: 开关 (从设置读取)
   ├── 保存位置: [浏览文件夹]
   ├── 文件名: [可编辑]
   └── [确认打印] / [取消]
⑥ 用户点击 [确认打印]
⑦ Ghostscript 开始转换:
   ├── 根据格式/颜色/分辨率生成输出
   ├── 如启用水印, 叠加水印图层
   └── 保存到目标位置
⑧ 转换完成:
   ├── 成功 → 弹出提示 "打印成功" + 可选打开文件夹
   └── 失败 → 弹出提示 "打印失败" + 错误信息
⑨ 清理临时文件
```

### 2.4 系统架构

#### 2.4.1 总体流程

```
[应用程序] → [PostScript 驱动] → [端口监视器 DLL] → [后台服务] → [Ghostscript] → [输出文件]
                                          ↓
                                    [临时 PS 文件]
```

#### 2.4.2 组件关系

```
┌──────────────────────────────────────────────────────────┐
│                     VirtualPrinter                        │
│                                                           │
│  ┌─────────────────────────────┐                          │
│  │ VirtualPrinterManager       │  (开始菜单启动)          │
│  │ (WPF, 打印机管理工具)        │                          │
│  │  · 安装打印机 (环境检测)     │                          │
│  │  · 卸载打印机 (端口/驱动)   │                          │
│  │  · 全局默认设置             │                          │
│  └─────────────────────────────┘                          │
│                                                           │
│  ┌─────────────────────────────┐                          │
│  │ VirtualPrinterSaveDialog    │  (打印时由服务唤起)       │
│  │ (WPF, 打印设置对话框)       │                          │
│  │  · 格式/颜色/分辨率        │                          │
│  │  · 水印开关/多页处理       │                          │
│  │  · 文件名/保存位置         │                          │
│  └──────────┬──────────────────┘                          │
│             │ (IPC: Named Pipe)                           │
│             ▼                                             │
│  ┌─────────────────────────────┐                          │
│  │ VirtualPrinterService       │                          │
│  │ (Windows Service)           │                          │
│  │  · 作业队列管理             │                          │
│  │  · Ghostscript 调用         │                          │
│  │  · Session 0 UI 唤起       │                          │
│  └─────────────────────────────┘                          │
│             ▲                                             │
│             │ (Named Pipe)                                │
│  ┌──────────┴──────────────┐                             │
│  │ VPPostScriptMon         │                             │
│  │ (C++ Port Monitor DLL)  │                             │
│  │  · Spooler 接口         │                             │
│  │  · PS 数据捕获          │                             │
│  │  · 临时文件写入         │                             │
│  └─────────────────────────┘                             │
│                                                           │
│  ┌─────────────────────────────┐                          │
│  │ VPGhostLib                  │                          │
│  │ (C# GS 封装库)              │                          │
│  │  · GS 进程管理/参数构建     │                          │
│  └─────────────────────────────┘                          │
│                                                           │
│  ┌────────────────────────────────────────────────────┐  │
│  │  Installer (Inno Setup)                            │  │
│  │   - 释放文件 + 环境检测 + 注册驱动 + 安装服务      │  │
│  │   - 创建开始菜单: [打印机管理] [卸载]              │  │
│  └────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────┘
```

## 3. 组件详细设计

### 3.1 VPPostScriptMon（端口监视器 DLL）

**语言**: C++/Win32, 原生 DLL  
**位置**: `%SYSTEMROOT%\System32\VPPostScriptMon.dll`  
**类型**: Windows 端口监视器 (Port Monitor)

#### 3.1.1 实现的接口

端口监视器必须实现以下 Print Spooler API：

```c
// 必需导出函数
BOOL WINAPI InitializeMonitor(LPWSTR pszRegistryRoot);
BOOL WINAPI InitializePrintMonitor2(PMONITORINIT pMonitorInit, HANDLE* phMonitor);
BOOL WINAPI OpenPort(HANDLE hMonitor, LPWSTR pszName, HANDLE* phPort);
BOOL WINAPI StartDocPort(HANDLE hPort, LPWSTR pszPrinterName, DWORD jobId, DWORD level, LPBYTE pDocInfo);
BOOL WINAPI WritePort(HANDLE hPort, LPBYTE pBuffer, DWORD cbBuffer, LPDWORD pcbWritten);
BOOL WINAPI ReadPort(HANDLE hPort, LPBYTE pBuffer, DWORD cbBuffer, LPDWORD pcbRead);
BOOL WINAPI EndDocPort(HANDLE hPort);
BOOL WINAPI ClosePort(HANDLE hPort);
BOOL WINAPI ShutdownMonitor(HANDLE hMonitor);
// ... 其他可选接口
```

#### 3.1.2 工作流程

1. `InitializePrintMonitor2` → 初始化监视器实例，返回 `MONITOR2` 函数表
2. `OpenPort` → 打开端口，创建 `PortContext` 实例（`(HANDLE)ctx` 注册到全局 `g_ports` map）
3. `StartDocPort` → 打印作业开始, 创建临时文件: `%ProgramData%\VirtualPrinter\{JobId}.tmp`
4. `WritePort` → 接收 PostScript 数据, 写入临时文件
5. `EndDocPort` → 打印作业结束, 关闭文件句柄, 启动分离线程 (detached std::thread) 通过 Named Pipe 通知后台服务
6. `ClosePort` → 关闭端口，销毁 PortContext

#### 3.1.3 Named Pipe 通信协议

端口监视器通过独立线程向后台服务发送指令（Pipe 超时 2 秒）：

```
管道名: \\.\pipe\VirtualPrinter
编码: Unicode (UTF-16LE)
指令格式: JSON (不含 UserName/Timestamp 字段)
{
  "Action": "NewJob",
  "JobId": 123,
  "TempFile": "C:\\ProgramData\\VirtualPrinter\\123.tmp",
  "DocumentName": "我的文档.docx",
  "PrinterName": "YanziWu PDF-IMG Printer"
}
```

服务端 `PipeListener` 监听 `\\.\pipe\VirtualPrinter`，读取 Unicode 编码的 JSON。
SaveDialog 结果通过另一个管道回传：

```
管道名: \\.\pipe\VirtualPrinterResult
编码: UTF-8
报文: {"Action":"JobResult","JobId":123,"OutputPath":"...","Success":true,"Error":null}
```

### 3.2 VirtualPrinterService（后台处理服务）

**语言**: C# .NET Framework 4.8  
**类型**: Windows Service + 前台辅助进程

#### 3.2.1 核心模块

| 模块 | 职责 |
|------|------|
| `PipeListener` | 监听 Named Pipe `\\.\pipe\VirtualPrinter`（Unicode 编码），接收端口监视器的通知 |
| `ResultPipeListener` | 监听 Named Pipe `\\.\pipe\VirtualPrinterResult`（UTF-8 编码），接收 SaveDialog 结果 |
| `JobProcessor` | 管理打印作业队列（`ConcurrentQueue<PrintJob>`），通过 `TaskCompletionSource<JobResult>` 异步等待结果 |
| `ConfigManager` | 读写 `%APPDATA%\VirtualPrinter\settings.json`（JavaScriptSerializer） |
| `FileNamingEngine` | 根据规则生成文件名（Token: {DocumentName}/{Date}/{Time}/{DateTime}/{JobId}/{Format}） |
| `TempFileManager` | 临时文件清理（15 分钟过期，5 分钟周期，扫描 .ps/.pending/.tmp） |
| `SessionLauncher` | 跨 Session 0 启动器（P/Invoke: WTSQueryUserToken → DuplicateTokenEx → CreateProcessAsUser） |

#### 3.2.2 工作流程

```
1. PipeListener 收到 NewJob 通知（Unicode JSON）
2. JobProcessor 将任务加入 `ConcurrentQueue<PrintJob>`
3. 尝试通过 `SessionLauncher.LaunchInActiveSession` 启动 VirtualPrinterSaveDialog.exe 到用户 Session（失败时回退 `Process.Start`）
4. 保存对话框以模态置顶窗口显示:
   - 用户选择输出格式 (PDF/PNG/JPEG/BMP/TIFF)
   - 选择保存路径和文件名
   - 设置分辨率/颜色模式/分页方式
   - [打印] → SaveDialog 直接在当前进程调用 GSConvert 转换
   - [取消] → 发送取消结果到 `\\.\pipe\VirtualPrinterResult`
5. GSConvert (VPGhostLib) 调用 gswin64c.exe:
   - PDF:   gswin64c -sDEVICE=pdfwrite -dCompatibilityLevel=1.7 -o output.pdf input.ps
   - PNG:   gswin64c -sDEVICE=png16m -r300 -o output_%d.png input.ps
   - JPEG:  gswin64c -sDEVICE=jpeg -r300 -dJPEGQ=85 -o output_%d.jpg input.ps
   - BMP:   gswin64c -sDEVICE=bmp16m -r300 -o output_%d.bmp input.ps
   - TIFF:  gswin64c -sDEVICE=tiff24nc -r300 -o output.tiff input.ps
   (多页图片: %d 自动分页; 多页 TIFF: 合并为单文件)
6. 如启用水印: `WatermarkEngine.Apply` 根据格式自动派发（图片用 `System.Drawing.Graphics.DrawString`，PDF 用 Ghostscript setpagedevice 叠加）
7. 转换结果通过 `ServiceClient.SendResultAsync` 发送到 `\\.\pipe\VirtualPrinterResult`（UTF-8 JSON）
8. 服务端 `ResultPipeListener` 接收结果，通过 `TaskCompletionSource<JobResult>` 完成异步等待
9. 清理临时文件（.ps 和 .pending）
10. 打开输出文件夹 (可选)
```

#### 3.2.3 Session 0 隔离处理

Windows Service 运行在 Session 0，无法直接显示 UI。解决方案：

- **方案**: 服务无常驻 UI，收到打印任务时，先尝试 `SessionLauncher.LaunchInActiveSession`（使用 `WTSQueryUserToken` + `DuplicateTokenEx` + `CreateProcessAsUser`），失败时回退到普通 `Process.Start`
- 服务通过 Named Pipe `\\.\pipe\VirtualPrinter` 接收作业通知，通过 `\\.\pipe\VirtualPrinterResult` 接收结果回传
- 对话框以模态置顶窗口显示（`Topmost=True`），10 分钟无操作自动关闭
- 对话框处理完毕后进程通过三重退出保证终止（`Close()` → `Application.Current.Shutdown()` → `Environment.Exit(0)`）
- **启动静默期**: 服务启动后前 15 秒的作业静默丢弃，避免后台处理程序重放过期作业

#### 3.2.4 保存对话框 UI 设计

```
┌──────────────────────────────────────────┐
│  VirtualPrinter - 保存打印输出    — □ ×  │
│──────────────────────────────────────────│
│  📄 文档: 报告.docx                       │
│  打印于: 2026-01-01 12:00                │
│                                          │
│  输出格式: [PDF                   ▼]     │
│            PDF                           │
│            PNG        颜色: [RGB ▼]      │
│            JPEG       分辨率: [300] DPI  │
│            BMP        [高级设置...]      │
│            TIFF                          │
│                                          │
│  ┌─ 多页处理 ──────────────────────────┐ │
│  │ ○ PDF: 合并为单文件                  │ │
│  │ ○ 图片: 每页单独文件                 │ │
│  │ ● 图片: 合并为多页 TIFF             │ │
│  └──────────────────────────────────────┘ │
│                                          │
│  ┌─ 水印 ──────────────────────────────┐ │
│  │  ☑ 启用水印                         │ │
│  │  文字: [机密文件                ]    │ │
│  │  字体: [微软雅黑 ▼] 大小: [48]     │ │
│  │  位置: [居中 ▼]  透明度: [30%]      │ │
│  │  颜色: [████]  ☑ 首页不显示         │ │
│  └──────────────────────────────────────┘ │
│                                          │
│  保存到: [C:\打印输出\           ] [浏览]│
│  文件名: [报告_20260101_120000        ]  │
│                                          │
│  ☐ 记住这些设置                          │
│  ☑ 打印后打开输出文件夹                  │
│                                          │
│        [打印]        [取消]              │
└──────────────────────────────────────────┘
```

**点击 [高级设置...] 弹窗 (格式切换时动态显示):**
```
┌─ 图片高级设置 ──────────────────────┐
│  JPEG 质量: [━━━━━●━━━━━] 85%       │
│  PNG 压缩: [自动 ▼]                 │
│  背景色: ○ 白色 ● 透明 (PNG)        │
│  色彩深度: [24bit 真彩 ▼]           │
│  ☑ 嵌入 ICC 色彩配置文件            │
└─────────────────────────────────────┘

┌─ PDF 高级设置 ──────────────────────┐
│  PDF 版本: [1.7 ▼]                  │
│  嵌入字体: ☑                         │
│  压缩: [自动 ▼]                     │
└─────────────────────────────────────┘
```

### 3.3 VirtualPrinterManager（打印机管理工具）

**语言**: C# .NET Framework 4.8 WPF  
**类型**: 打印机安装/卸载管理工具 (从开始菜单启动, 自提升管理员权限, 无常驻)  
**专注**: 只负责打印机安装/卸载和环境管理, 不包含打印输出设置

#### 3.3.1 功能模块

| 模块 | 功能 |
|------|------|
| `EnvDetector` (内联) | 系统环境检测 (.NET/VC++/Spooler/Canon PS3 驱动)，通过注册表/WMI/ServiceController 检测 |
| `PrinterInstaller` | 安装打印机 (部署 DLL → 注册表端口 → 安装 Canon 驱动 → 创建打印机 → 安装服务，含自动回滚) |
| `PrinterUninstaller` | 卸载打印机 (停止服务 → 删除打印机 → 按勾选删除端口/驱动) |
| `DefaultSettings` | 全局默认值 (默认保存目录、默认格式、水印默认文本)，读写 settings.json |
| `VersionInfo.cs` | 版本和测试批次信息（由 build.bat 生成） |

#### 3.3.2 UI 界面布局

```
┌──────────────────────────────────────────────────────┐
│  VirtualPrinter 管理工具                   — □ ×    │
├──────────┬───────────────────────────────────────────┤
│          │  ┌─ 打印机状态 ──────────────────────┐   │
│ 📠 管理   │  │  📠 VirtualPrinter ● 已安装       │   │
│ ℹ 关于   │  │  端口: VP_Port                    │   │
│          │  │  驱动: Canon Generic Plus PS3   │   │
│          │  │  服务: VirtualPrinterService ● 运行│   │
│          │  │                                    │   │
│          │  │  ┌─ 环境检测 ───────────────────┐ │   │
│          │  │  │  ✓ .NET Framework 4.8       │ │   │
│          │  │  │  ✓ VC++ Redist 14.x         │ │   │
│          │  │  │  ✓ Print Spooler 运行中     │ │   │
│          │  │  │  ✓ PostScript 驱动已安装    │ │   │
│          │  │  │  ✓ Ghostscript 就绪         │ │   │
│          │  │  └──────────────────────────────┘ │   │
│          │  │                                    │   │
│          │  │  [安装打印机]  [刷新状态]          │   │
│          │  │                                    │   │
│          │  │  ┌─ 卸载选项 ───────────────────┐ │   │
│          │  │  │  ☑ 同时删除端口 (VP_Port)    │ │   │
│          │  │  │  ☑ 同时删除打印机驱动        │ │   │
│          │  │  │  [卸载打印机]                 │ │   │
│          │  │  └──────────────────────────────┘ │   │
│          │  └──────────────────────────────────┘   │
│          │                                          │
│          │  ┌─ 全局默认值 ────────────────────┐   │
│          │  │  默认保存目录: [C:\打印输出\    │   │
│          │  │  默认格式: [PDF ▼]              │   │
│          │  │  水印默认文本: [机密文件      ] │   │
│          │  │  (水印开关和详细内容在 SaveDialog 中设置)│
│          │  └────────────────────────────────────┘   │
├──────────┴───────────────────────────────────────────┤
│  [应用]                                              │
└──────────────────────────────────────────────────────┘
```

### 3.4 Installer（安装程序）

**工具**: Inno Setup  
**输出**: `VirtualPrinter_Setup.exe` (单文件)

#### 3.4.1 安装步骤

```
1. 检测系统版本 (Win7+)，检测 .NET 4.8 / VC++ Redist / Spooler 状态
2. Inno Setup Pascal 脚本执行环境检测，失败时阻止安装
3. 复制 VPPostScriptMon.dll 到 %SYSTEMROOT%\System32\ (regserver)
4. 若 Win7 且 .NET 4.8 缺失，静默下载安装 ndp48-x86-x64-allos-enu.exe
5. 若 VC++ Redist 缺失，静默安装 vc_redist.x64.exe（Win10/11 内嵌，Win7 从 CDN下载）
6. 通过 pnputil 安装 Canon Generic Plus PS3 驱动 (CNS30MA64.INF)
7. 通过 printui.dll 创建打印机 "YanziWu PDF-IMG Printer"，端口 VP_Port
8. 通过 sc.exe 创建并启动 VirtualPrinterService
9. 复制 Ghostscript (gsdll64.dll, gswin64c.exe) 到安装目录
10. 创建开始菜单快捷方式 (管理工具 + 卸载)
11. 可选创建桌面快捷方式
```

#### 3.4.2 卸载步骤

```
1. 停止 VirtualPrinterService
2. 删除打印机
3. 删除端口
4. 注销端口监视器
5. 删除 DLL
6. 删除服务
7. 删除安装目录和临时文件
```

## 4. 配置文件

存储位置: `%APPDATA%\VirtualPrinter\settings.json`

```json
{
  "version": "1.0",
  "defaultFormat": "pdf",
  "resolution": 300,
  "colorMode": "color",
  "saveDialog": true,
  "saveFolder": "C:\\打印输出",
  "fileNamingRule": "{DocumentName}_{Date}_{Time}",
  "jpegQuality": 85,
  "pngCompression": "auto",
  "backgroundColor": "#FFFFFF",
  "pngTransparency": false,
  "openFolderAfterPrint": true,
  "language": "zh-CN",
  "maxHistoryCount": 100
}
```

## 5. Ghostscript 集成

### 5.1 设备映射

| 输出格式 | RGB | Grayscale | CMYK | BlackWhite |
|---------|-----|-----------|------|------------|
| PDF | `pdfwrite` | `pdfwrite` | `pdfwrite` | `pdfwrite` |
| PNG | `png16m` | `pnggray` | `png16m` | `pngmono` |
| JPEG | `jpeg` | `jpeggray` | `jpegcmyk` | `jpegmono` |
| BMP | `bmp16m` | `bmpgray` | `bmp16m` | `bmpmono` |
| TIFF | `tiff24nc` | `tiffgray` | `tiff32nc` | `tiffg4` |

**关键参数**:
- PDF: `-dPDFSETTINGS=/prepress -dCompatibilityLevel=1.7 -dEmbedAllFonts=true`
- JPEG: `-r{resolution} -dJPEGQ={quality}`
- 图片: `-r{resolution} -dTextAlphaBits=4 -dGraphicsAlphaBits=4`
- 黑白 TIFF: `-dDownScaleColorImages=false -dDownScaleGrayImages=false`
- 多页图片: `-sOutputFile="output_%d.ext"`（%d 自动分页）

### 5.2 GS 调用示例

```
PDF (基础版, 嵌入字体):
  gswin64c.exe -dNOPAUSE -dBATCH -dQUIET -sDEVICE=pdfwrite
    -dPDFSETTINGS=/prepress -dCompatibilityLevel=1.7
    -dEmbedAllFonts=true -dSubsetFonts=true
    -dAutoFilterColorImages=true -dColorImageDownsampleType=/Bicubic
    -sOutputFile="output.pdf" -f "input.ps"

PNG (RGB, 300 DPI, 多页分页):
  gswin64c.exe -dNOPAUSE -dBATCH -dQUIET -sDEVICE=png16m
    -r300 -dTextAlphaBits=4 -dGraphicsAlphaBits=4
    -sOutputFile="output_%d.png" -f "input.ps"

PNG (黑白二值, 300 DPI):
  gswin64c.exe -dNOPAUSE -dBATCH -dQUIET -sDEVICE=pngmono
    -r300 -dTextAlphaBits=4
    -sOutputFile="output_%d.png" -f "input.ps"

JPEG (RGB, quality 85, 300 DPI):
  gswin64c.exe -dNOPAUSE -dBATCH -dQUIET -sDEVICE=jpeg
    -r300 -dJPEGQ=85 -dTextAlphaBits=4 -dGraphicsAlphaBits=4
    -sOutputFile="output_%d.jpg" -f "input.ps"

TIFF (CMYK, 300 DPI, 多页合并):
  gswin64c.exe -dNOPAUSE -dBATCH -dQUIET -sDEVICE=tiff32nc
    -r300 -dTextAlphaBits=4 -dGraphicsAlphaBits=4
    -sOutputFile="output.tiff" -f "input.ps"

TIFF (黑白二值 CCITT G4, 多页合并):
  gswin64c.exe -dNOPAUSE -dBATCH -dQUIET -sDEVICE=tiffg4
    -r300
    -sOutputFile="output.tiff" -f "input.ps"

### 5.3 Rasterize-to-PDF 模式

对于复杂 PostScript 文件，支持两步渲染模式 (`GSConvertOptions.RasterizeToPdf`):
1. Ghostscript 将 PS 渲染为逐页 PNG 图片（临时目录）
2. 纯 C# PDF 构建器 (`CreatePdfFromImages`) 使用原生 PDF 语法构建 PDF 1.4 文件
   - 每页 PNG 转为 JPEG 压缩嵌入
   - 构建对象树、交叉引用表和文件尾
   - 不依赖任何外部 PDF 库

### 5.4 图片拼接

当选择"合并为单文件"且输出为图片格式（非 TIFF）时，`StitchImages` 方法将多页图片合并为一张长图：
1. 计算所有页面总高度
2. 创建合并位图
3. 逐页使用 `Graphics.DrawImage` 绘制
4. 使用对应格式编码器保存

### 5.5 水印叠加方案

`WatermarkEngine` 根据文件格式自动派发：

**图片水印**（PNG/JPEG/BMP/TIFF）:
- 使用 `System.Drawing.Graphics.DrawString` 叠加文字
- `alpha = opacity * 255 / 100`，灰度色 RGB(128,128,128)
- 6 种位置: Center, TopLeft, TopRight, BottomLeft, BottomRight, Tile

**PDF 水印**:
- 利用 Ghostscript `setpagedevice` + `EndPage` 过程在每页覆盖文字
```postscript
<< /EndPage {
    2 eq { pop false } {
        gsave /Helvetica-Bold 36 selectfont alpha setgray pos moveto (text) show grestore true
    } ifelse
} >> setpagedevice
```

## 6. 项目文件结构

```
VirtualPrinter/
├── DESIGN.md                          # 本设计文档
├── README.md                          # 项目说明
│
├── src/
│   ├── VPPostScriptMon/               # 端口监视器 DLL (C++17, CMake)
│   │   ├── CMakeLists.txt
│   │   ├── DllMain.cpp                # DLL 入口 + 3 个导出函数
│   │   ├── Monitor.cpp/.h             # MONITOR2 函数表 (22 个回调)
│   │   ├── Port.cpp/.h                # PortContext: StartDoc → Write → EndDoc
│   │   ├── PipeClient.cpp/.h          # Named Pipe 客户端 (2s 超时)
│   │   └── stdafx.h                   # 预编译头
│   │
│   ├── VirtualPrinterService/         # 后台服务 (C# net48)
│   │   ├── VirtualPrinterService.csproj
│   │   ├── Program.cs                 # 服务入口
│   │   ├── MainService.cs             # 服务主逻辑 + OnStart/OnStop
│   │   ├── ProjectInstaller.cs        # 服务安装器 (LocalSystem)
│   │   ├── Services/
│   │   │   ├── PipeListener.cs        # \\.\pipe\VirtualPrinter (Unicode)
│   │   │   ├── ResultPipeListener.cs  # \\.\pipe\VirtualPrinterResult (UTF-8)
│   │   │   ├── JobProcessor.cs        # 作业队列 + SessionLauncher 调用
│   │   │   └── ConfigManager.cs       # settings.json 读写
│   │   ├── Models/
│   │   │   ├── PrintJob.cs            # 打印作业模型 + JSON 反序列化
│   │   │   ├── JobResult.cs           # 结果模型
│   │   │   └── Settings.cs            # 配置模型
│   │   └── Utils/
│   │       ├── FileNamingEngine.cs    # 文件名 Token 替换
│   │       ├── SessionLauncher.cs     # P/Invoke 跨 Session 启动
│   │       └── TempFileManager.cs     # 定时清理 (15min 过期)
│   │
│   ├── VirtualPrinterManager/         # 管理工具 (C# WPF net48)
│   │   ├── VirtualPrinterManager.csproj
│   │   ├── App.xaml
│   │   ├── MainWindow.xaml/.cs        # 安装/卸载/环境检测 (自提升管理员)
│   │   ├── ProgressWindow.xaml/.cs    # 安装进度窗口
│   │   └── VersionInfo.cs             # 版本 + 批次 (由 build.bat 生成)
│   │
│   ├── VirtualPrinterSaveDialog/      # 保存对话框 (C# WPF net48)
│   │   ├── VirtualPrinterSaveDialog.csproj
│   │   ├── App.xaml                   # 配色方案 + 全局样式 (方案3 朴素风格)
│   │   ├── MainWindow.xaml/.cs        # 格式/颜色/水印/保存配置
│   │   ├── AdvancedSettingsWindow.xaml/.cs # JPEG 质量/PDF 版本/嵌入字体
│   │   ├── ProgressWindow.xaml/.cs    # 转换进度
│   │   └── Services/
│   │       ├── ServiceClient.cs       # 结果回传 (UTF-8 JSON → \\.\pipe\VirtualPrinterResult)
│   │       └── WatermarkEngine.cs     # 图片/PDF 水印叠加
│   │
│   ├── VPGhostLib/                    # Ghostscript 封装库 (C# net48)
│   │   ├── VPGhostLib.csproj
│   │   └── GSConvert.cs              # 统一入口: Convert + 21 种设备映射 + RasterizeToPdf
│   │
│   ├── VirtualPrinterLauncher/        # 自解压启动器 (C# net48)
│   │   ├── VirtualPrinterLauncher.csproj
│   │   ├── Program.cs                 # 解压 bundle.zip → 提权启动 Manager
│   │   └── Resources/bundle.zip       # 内嵌压缩包
│   │
│   ├── RedmonRedirector/              # Redmon 兼容重定向器 (C++, CMake)
│   │   ├── CMakeLists.txt
│   │   └── main.cpp                   # stdin → 文件 + 通知服务
│   │
│   └── VirtualPrinter.Tests/         # 单元测试 (xUnit, net48)
│
├── installer/
│   ├── setup.iss                      # Inno Setup 脚本 (含 Pascal 检测逻辑)
│   ├── components/
│   │   └── EnvChecker.dll/h/cpp       # 环境检测原生 DLL (C++)
│   ├── drivers/
│   │   └── Canon/                     # Canon Generic Plus PS3 驱动 (CNS30MA64)
│   └── assets/
│
├── lib/
│   └── gs/                            # Ghostscript 运行时 (gswin64c.exe + gsdll64.dll)
│
├── tests/
│   ├── GsTest/Program.cs              # GS 转换测试台 (700 行)
│   ├── TestPipeline/Program.cs        # 端到端管道测试 (net9.0)
│   └── addport.cs                     # XcvData 端口添加测试
│
├── assets/
│   └── printer.ico                    # 应用程序图标
│
├── build.bat                          # 一键构建脚本 (194 行)
├── DESIGN.md                          # 本设计文档
├── README.md                          # 项目说明
├── TECHNICAL_DOCUMENTATION.md         # 技术文档
├── VirtualPrinter.sln                 # Visual Studio 解决方案
├── session-ses_099b.md                # 开发会话记录
└── qc                                 # QC 标识文件
```

## 7. 关键技术难点与解决方案

### 7.1 端口监视器 DLL 开发

**难点**: 需要 C++ 原生 DLL，实现完整的 Print Spooler Port Monitor API

**方案**: 
- 使用 Win32 项目模板，最小化依赖
- 参考 Windows SDK 中的 `localspl.h` 和示例代码
- 关键 API: `AddMonitor`, `DeleteMonitor`, 端口监视器接口

### 7.2 Session 0 隔离

**难点**: Windows Service 运行在 Session 0，无法直接显示 UI

**方案**:
- 使用 `CreateProcessAsUser` 在用户 Session 中启动 `VirtualPrinterSaveDialog.exe`
- 通过 Named Pipe `\\.\pipe\VirtualPrinter_{SessionId}` 通信
- 使用 `WTSGetActiveConsoleSessionId` 获取当前用户 Session

### 7.3 Ghostscript 分发

**难点**: Ghostscript 需要授权分发

**方案**:
- Ghostscript 是 AGPL 协议，可以免费分发
- 在安装包中打包 `gsdll64.dll` 和 `gswin64c.exe`
- 使用 `GPL Ghostscript` 发行版

### 7.4 Windows 7 兼容性

**要求**:
- Win7 SP1 是支持的最低版本
- .NET Framework 4.8 (Win7 不自带, 安装程序自动检测并静默安装)
- PostScript 驱动使用 Canon Generic Plus PS3 (V3)，已内嵌驱动包 (CNS30MA64.INF + gpps3.cab)
- VC++ Redist 2015-2022 (Win7 不自带, 安装程序自动安装)
- 端口监视器通过注册表注册（非 AddMonitor API），在 Win7-Win11 上一致
- 使用 Win32 API 的 subset 兼容所有版本
- **注意**: Canon 驱动仅支持 x64，不支持 ARM64

### 7.5 打印机驱动安装需要管理员

**难点**: 安装打印机需要管理员权限

**方案**:
- 安装程序以管理员权限运行
- 使用 `INNO SETUP` 的 `PrivilegesRequired=admin`
- 通过 `rundll32.exe printui.dll,PrintUIEntry` 安装打印机

### 7.6 环境检测与自动补全引擎

**难点**: 需要在不同 Windows 版本上准确检测缺失的运行时和组件, 并静默自动安装

**方案**:
- 环境检测引擎由两部分组成:
  1. **Inno Setup Pascal 脚本** (installer/detection.iss): 负责界面流程和安装逻辑
  2. **EnvChecker.dll** (C++ 原生 DLL): 负责底层系统检测, 被 Inno Setup 调用
- 检测 DLL 通过 Win32 API 直接查询注册表、服务状态、文件系统
- 修复操作按优先级排序, 先修复致命项, 再修复高优先级项
- 所有修复操作记录详细日志, 便于排查

**EnvChecker.dll API**（实际导出函数，见 `EnvChecker.h`）:
```c
HRESULT WINAPI CheckOSVersion(DWORD* major, DWORD* minor, DWORD* build, BOOL* isServer);
HRESULT WINAPI CheckArchitecture(WCHAR* arch, DWORD archSize);
HRESULT WINAPI IsAdmin(BOOL* admin);
HRESULT WINAPI CheckDotNetFramework(DWORD* releaseVersion, BOOL* installed);
HRESULT WINAPI CheckVCRedist(BOOL* installed, DWORD* versionMajor);
HRESULT WINAPI CheckPrintSpooler(BOOL* running);
HRESULT WINAPI CheckDiskSpace(LPCWSTR path, DWORD64 requiredMB, BOOL* sufficient);
```

## 8. 环境检测与自动补全

### 8.1 检测流程总览

```
┌─────────────────────────────────────────────────────┐
│                VirtualPrinter 安装程序                 │
├─────────────────────────────────────────────────────┤
│  1. 系统环境检测                                       │
│     ├─ Windows 版本 (7/10/11)                         │
│     ├─ 系统架构 (x64/ARM64)                           │
│     ├─ 管理员权限                                     │
│     └─ 磁盘空间 (>500MB)                              │
│                                                       │
│  2. 运行时依赖检测                                      │
│     ├─ .NET Framework 4.8  [Win7需安装]             │
│     ├─ VC++ Redist 2015-2022 x64     [按需安装]         │
│     ├─ Print Spooler 服务运行中       [自动启动]          │
│     └─ Ghostscript                    [已打包]          │
│                                                       │
│  3. 打印机组件检测                                      │
│     ├─ PostScript 打印机驱动可用       [按需安装]          │
│     ├─ 端口监视器已注册               [自动注册]          │
│     └─ 打印机端口 "VP_Port" 已创建    [自动创建]          │
│                                                       │
│  4. 冲突检测                                           │
│     ├─ 已有 VirtualPrinter 安装       [覆盖/升级]        │
│     ├─ 端口冲突                      [提示处理]          │
│     └─ 打印机名冲突                   [自动重命名]        │
│                                                       │
│  ┌────────────────────────────────────────────────────┐ │
│  │  所有检测通过 → 执行安装                           │ │
│  │  检测失败 → 自动修复 → 重试 → 如仍失败 → 报告错误    │ │
│  └────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────┘
```

### 8.2 详细检测清单

| 检测项 | 检测方法 | 通过条件 | 自动修复措施 | 严重性 |
|--------|---------|---------|-------------|--------|
| Windows 版本 | `GetVersionEx` / RtlGetVersion | Win7 SP1+, Win10 1607+, Win11 | 不通过则阻止安装 | 致命 |
| 系统架构 | `GetNativeSystemInfo` | x64 (AMD64) | 提示不兼容 | 致命 |
| 管理员权限 | `IsUserAnAdmin` | TRUE | 提示以管理员运行 | 致命 |
| 磁盘空间 | `GetDiskFreeSpaceEx` | 安装目录 > 500MB | 提示空间不足 | 致命 |
| .NET Framework | 注册表 `Release` 键值 | >= 528040 (4.8) | Win7: 静默安装 .NET 4.8 | 高 |
| VC++ Redist | 注册表 VC 运行库键 | 版本 >= 14.0 | 静默安装 vc_redist.x64.exe | 高 |
| Print Spooler | `QueryServiceStatus` | 服务运行中 | `StartService` 自动启动 | 高 |
| Canon PS3 Driver | WMI `Win32_PrinterDriver` | 已安装 | pnputil + printui.dll 安装 | 高 |
| 端口监视器 DLL | 注册表 `Monitors\VirtualPrinter Port Monitor` + 文件存在 | DLL 存在且注册表键存在 | 复制 DLL 到 System32 + 写入注册表 | 高 |
| Ghostscript | 注册表 + 文件存在 | gswin64c.exe 可执行 | 从安装包释放 | 高 |

### 8.3 自动修复实现

#### 8.3.1 .NET Framework 4.8 自动安装 (Win7 场景)

```
检测逻辑:
  Win10/11: .NET Framework 4.8 已内置, 无需操作
  Win7: 读取注册表 HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full
    - Release >= 528040 (4.8) → 已安装
    - 否则 → 需要安装

安装方式:
  1. 从微软官方 CDN 或国内镜像下载离线安装包
  2. 下载地址:
     - 微软官方: https://download.microsoft.com/download/.../ndp48-x86-x64-allos-enu.exe
     - 国内镜像: https://dotnet.microsoft.com/zh-cn/download/dotnet-framework/thank-you/net48-web-installer
  3. 参数: /q /norestart /chainingpackage VirtualPrinter
  4. 下载进度在安装界面显示

注意事项:
  - 仅 Win7 需要下载安装; Win10/11 内置 .NET Framework, 跳过此步骤
  - 安装可能需要重启, 记录状态, 安装完成后继续
  - Win7 需要 SP1 + KB3063858 (SHA-2 支持)
  - 如 SP1 缺失, 引导用户安装 Win7 SP1
  - 下载失败时提供手动下载链接和 MD5 校验信息
  - 支持断点续传 (HTTP Range)
```

#### 8.3.2 VC++ Redist 自动安装

```
检测逻辑:
  读取注册表 HKLM\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64
    - Installed = 1 → 已安装
    - Major >= 14 → 已安装

安装方式:
  1. Win10/11: 内嵌 vc_redist.x64.exe 在安装包中 (约 15MB)
  2. Win7: 从微软官方 CDN 或国内镜像下载
     - 微软: https://aka.ms/vs/17/release/vc_redist.x64.exe
     - 国内镜像: https://download.microsoft.com/download/.../vc_redist.x64.exe
  3. 静默参数: /install /quiet /norestart
  4. 下载失败时: 提示用户手动安装, 提供链接
```

#### 8.3.3 PostScript 驱动自动安装 (Canon Generic Plus PS3)

```
驱动包来源:
  内嵌 Canon Generic Plus PS3 (V3) 驱动包:
  installer\drivers\Canon\ 目录包含:
    ├── CNS30MA64.INF      # Canon PS3 驱动 INF
    ├── gpps3.cab          # 驱动 CAB 包（含 DLL/PPD/字体）
    └── ...                # 解压后由 setup.iss 的 [Files] 释放

检测逻辑:
  WMI: SELECT * FROM Win32_PrinterDriver WHERE Name LIKE '%Canon Generic Plus PS3%'
  (由 VirtualPrinterManager 的 CheckPSDriverInstalled 方法实现)

安装方式:
  1. 解压 gpps3.cab 到驱动目录 (expand 命令)
  2. pnputil /add-driver "CNS30MA64.INF"  — 添加到驱动存储区
  3. rundll32 printui.dll,PrintUIEntry /if /b "YanziWu PDF-IMG Printer"
       /f "CNS30MA64.INF" /r "VP_Port" /m "Canon Generic Plus PS3"

注意事项:
  - Canon Generic Plus PS3 是完整的 PostScript 3 仿真驱动
  - 比 Microsoft PS Class Driver 提供更好的兼容性和字体支持
  - 仅支持 x64 架构
  - 驱动包从 Canon 官方 SDK 获取, 内嵌在安装包中
```

### 8.4 检测报告格式 (Pascal Script / Inno Setup)

```
检测结果结构:
  TEnvCheckResult = record
    Passed: Boolean;
    FailedItems: array of TFailedItem;
  end;

  TFailedItem = record
    Name: string;          // 检测项名称
    Severity: TSeverity;   // fatal, high, low
    AutoFixable: Boolean;  // 能否自动修复
    FixMethod: string;     // 修复方法标识
    Message: string;       // 错误描述
  end;
```

### 8.5 安装界面流程 (Inno Setup Wizard)

```
页面 1: 欢迎 → 检测开始
页面 2: [进度条] 正在检测系统环境...
          ├── ✓ Windows 版本: Windows 10 x64
          ├── ✓ 管理员权限: 是
          ├── ✓ 磁盘空间: 1.2TB 可用
          ├── ✓ .NET Framework: 4.8 (已就绪)
          ├── ✓ VC++ Redist: 已安装
          ├── ✓ Print Spooler: 运行中
          ├── ✓ Ghostscript: 已打包
          ├── ⚠ PostScript 驱动: 未安装 → [正在修复...]
          └── ✓ 端口监视器: 就绪
          [全部就绪] → 下一步
页面 3: 安装选项 (开始菜单, 自动启动等)
页面 4: 安装进度
页面 5: 完成 (启动配置工具)
```

### 8.6 失败处理策略

| 场景 | 处理方式 |
|------|---------|
| .NET 安装失败 | 提示手动安装, 提供官方下载链接 + MD5 |
| VC++ Redist 安装失败 | 提示手动安装, 提供官方下载链接 |
| PostScript 驱动安装失败 | 提示检查驱动包完整性, 手动 pnputil 安装 |
| Print Spooler 无法启动 | 提示检查系统服务 |
| 磁盘空间不足 | 提示清理磁盘 |
| 权限不足 | 提示以管理员运行 |
| 文件被占用 | 提示关闭冲突程序, 可选强制替换 |
| 网络下载失败 | 显示详细错误 + 镜像列表, 提供手动下载方式 |

### 8.7 内嵌 vs 下载策略总表

| 组件 | Win10/11 | Win7 |
|------|----------|------|
| Ghostscript (gswin64c.exe + gsdll64.dll) | ✅ 内嵌 | ✅ 内嵌 |
| VC++ Redist (vc_redist.x64.exe) | ✅ 内嵌 (~15MB) | ❌ 从 CDN 下载 |
| .NET Framework 4.8 | ✅ 系统内置, 跳过 | ❌ 从 CDN 下载 |
| PostScript 驱动 (INF + PPD) | ✅ 内嵌 | ✅ 内嵌 |
| 端口监视器 DLL | ✅ 内嵌 | ✅ 内嵌 |
| 后台服务 + GUI | ✅ 内嵌 | ✅ 内嵌 |

### 8.8 静默安装支持

```
命令行参数:
  /VERYSILENT   - 完全静默安装
  /SUPPRESSMSGBOXES - 抑制所有消息框
  /LOG="install.log" - 日志记录
  /NORESTART    - 安装完成后不重启

静默模式行为:
  1. 依赖检测 → 自动修复全部 → 安装 → 完成
  2. 如致命错误 → 写入日志, 返回非零退出码
  3. 错误码定义:
     0  - 成功
     1  - 用户取消
     2  - 系统不兼容 (OS/架构)
     3  - 依赖安装失败
     4  - 文件操作失败
     5  - 打印机安装失败
```

## 9. 依赖清单

| 依赖 | 版本 | 用途 | 分发方式 | 检测方法 |
|------|------|------|---------|---------|
| Ghostscript | 10.04.0+ | PS → PDF/Image 转换引擎 | 内嵌 `lib/gs/` (所有系统) | 文件系统搜索 9 个已知路径 |
| .NET Framework | 4.8 | C# 组件运行时 | Win10/11 内置; Win7 从 CDN 下载 | 注册表: `HKLM\...\NDP\v4\Full\Release >= 528040` |
| Canon PS3 驱动 | Generic Plus PS3 V3 | 生成 PostScript 数据 | 内嵌 `installer/drivers/Canon/` | WMI `Win32_PrinterDriver` |
| Print Spooler | 系统服务 | 打印队列管理 | 系统内置 | `ServiceController` (Spooler) |
| VC++ Redist | 2015-2022 x64 | C++ 端口监视器运行时 | Win10/11 内嵌; Win7 从 CDN 下载 | 注册表: `HKLM\...\VC\Runtimes\x64\Major >= 14` |
| Windows SDK | 10.0+ | 端口监视器开发 | 开发环境 | — |
| CMake | 3.10+ | C++ 项目构建 | 开发环境 | — |
| Visual Studio 2022 | 17.0+ | C++ 和 C# 编译 | 开发环境 | — |
| 7-Zip | 任意 | 构建时打包 bundle.zip | 开发环境 | — |

## 10. 构建与发布

### 10.1 开发环境配置

```
1. 安装 Visual Studio 2022 (含 C++ 和 .NET Framework 4.8 工作负载)
2. 安装 Windows SDK 10.0.20348.0+
3. 安装 CMake 3.10+
4. 下载 Ghostscript 10.04.0 发行版，放入 lib/gs/
5. 下载 VC++ Redist (可选)，放入 lib/vc_redist.x64.exe
6. 安装 Inno Setup 6.2+
7. 安装 7-Zip (用于创建 bundle.zip)
```

### 10.2 构建脚本 (build.bat，项目根目录)

```bash
# 一键构建所有组件
build.bat

# 构建步骤分解:
# Step 1: 构建 EnvChecker (C++ CMake)
cd installer\components
cmake -B build -A x64
cmake --build build --config Release

# Step 2: 构建 VPPostScriptMon (C++ CMake)
cd src\VPPostScriptMon
cmake -B build -A x64
cmake --build build --config Release

# Step 3: 构建 VPGhostLib (C#)
dotnet build src\VPGhostLib -c Release

# Step 4: 生成 VersionInfo.cs 并构建 C# 应用
# 构建 VirtualPrinterService + SaveDialog + Manager（均 target net48）
dotnet build src\VirtualPrinterService -c Release
dotnet build src\VirtualPrinterSaveDialog -c Release
dotnet build src\VirtualPrinterManager -c Release

# Step 5: 收集输出到 dist/bin/
# 复制 VPPostScriptMon.dll, EnvChecker.dll, 所有 EXE/DLL

# Step 6: 创建 bundle.zip (7-Zip)
# 嵌入到 VirtualPrinterLauncher 作为资源

# Step 7: 构建 VirtualPrinterLauncher (自解压启动器)
dotnet build src\VirtualPrinterLauncher -c Release

# Step 8: 构建安装包 (可选)
iscc installer\setup.iss
```

### 10.3 输出文件

| 文件 | 路径 | 说明 |
|------|------|------|
| `VirtualPrinter_Setup.exe` | `dist/` | Inno Setup 安装包 |
| `VirtualPrinterLauncher.exe` | `dist/` | 便携版自解压启动器 |
| `dist/bin/*` | `dist/bin/` | 核心运行时文件 |

## 11. 测试策略

| 测试类型 | 范围 | 工具/方法 | 实际文件 |
|---------|------|----------|---------|
| GS 转换测试 | VPGhostLib 管道 | GsTest (C# 控制台) | `tests/GsTest/Program.cs` (700 行) |
| 端到端管道测试 | 完整 PS→输出流程 | TestPipeline (C# WinForms) | `tests/TestPipeline/Program.cs` |
| 端口添加测试 | XcvData API | addport.cs (C# 控制台) | `tests/addport.cs` |
| 服务安装测试 | VirtualPrinterService | PowerShell 脚本 | `tests/_install_service.ps1` |

---

## 附录 A: 端口监视器 API 参考（实际实现）

端口监视器通过注册表注册（非传统的 AddMonitor API）：
- 注册表路径: `HKLM\SYSTEM\CurrentControlSet\Control\Print\Monitors\VirtualPrinter Port Monitor\`
- 键值: `Driver` = `VPPostScriptMon.dll`
- 端口枚举: `HKLM\...\Monitors\...\Ports\` 下的每个值名即端口名
- 支持 XcvData 查询: `Monitor`（返回监视器名）、`Port`（返回第一个端口）、`PortExists`、`AddPort`、`DeletePort`

```c
// MONITOR2 函数表 (实际 Monitor.h 实现，22 个回调)
typedef struct _MONITOR2 {
    DWORD cbSize;
    BOOL (*pfnEnumPorts)(...);           // 从注册表枚举端口
    BOOL (*pfnOpenPort)(...);            // 创建 PortContext
    BOOL (*pfnOpenPortEx)(...);          // 委托 OpenPort
    BOOL (*pfnStartDocPort)(...);        // 创建 .tmp 文件
    BOOL (*pfnWritePort)(...);           // 写入 PS 数据
    BOOL (*pfnReadPort)(...);            // 未实现 (返回 FALSE)
    BOOL (*pfnEndDocPort)(...);          // 关闭文件 → 通知服务
    BOOL (*pfnClosePort)(...);           // 销毁 PortContext
    BOOL (*pfnAddPort)(...);             // 存根
    BOOL (*pfnAddPortEx)(...);           // 存根
    BOOL (*pfnConfigurePort)(...);       // 存根
    BOOL (*pfnDeletePort)(...);          // 存根
    BOOL (*pfnGetPrinterDataFromPort)(...); // 未实现
    BOOL (*pfnSetPortTimeOuts)(...);     // 存根
    BOOL (*pfnXcvOpenPort)(...);         // 返回句柄 (HANDLE)1
    DWORD (*pfnXcvDataPort)(...);        // 注册表端口管理
    BOOL (*pfnXcvClosePort)(...);        // 存根
    VOID (*pfnShutdown)(...);            // 清理所有 PortContext
    DWORD (*pfnSendRecvBidiDataFromPort)(...); // 未实现
    DWORD (*pfnNotifyUsedPorts)(...);    // 存根
    DWORD (*pfnNotifyUnusedPorts)(...);  // 存根
    DWORD (*pfnPowerEvent)(...);         // 存根
} MONITOR2;

// PortContext 类结构 (Port.h)
class PortContext {
    std::wstring m_portName;       // 端口名称
    std::wstring m_tempFile;       // 临时文件路径 %ProgramData%\VirtualPrinter\{JobId}.tmp
    std::wstring m_documentName;   // 文档名称
    std::wstring m_printerName;    // 打印机名称
    DWORD m_jobId;                 // 打印作业 ID
    HANDLE m_hFile;                // 临时文件句柄
    bool m_inJob;                  // 作业进行中标志

    bool EnsureTempDir();          // 创建 %ProgramData%\VirtualPrinter\
    bool NotifyService();          // 启动分离线程通知服务
};
```

## 附录 B: 命名规则引擎

| 占位符 | 说明 | 示例 |
|--------|------|------|
| `{DocumentName}` | 文档名称 | 报告 |
| `{Date}` | 日期 (yyyyMMdd) | 20260101 |
| `{Time}` | 时间 (HHmmss) | 120000 |
| `{DateTime}` | 日期时间 | 20260101_120000 |
| `{JobId}` | 打印作业 ID | 123 |
| `{PrinterName}` | 打印机名称 | VirtualPrinter |
| `{UserName}` | 用户名 | zhangshan |
| `{Format}` | 输出格式 | pdf |

示例规则:
- `{DocumentName}_{DateTime}` → `报告_20260101_120000.pdf`
- `{Date}\{UserName}_{DocumentName}` → `20260101\zhangshan_报告.pdf`
