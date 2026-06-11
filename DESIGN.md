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
   └── PostScript 驱动 → 从安装包释放 INF/PPD 并注册
④ 注册端口监视器 (VPPostScriptMon.dll → system32)
⑤ 添加打印机端口 "VP_Port"
⑥ 安装打印机驱动 "Microsoft PS Class Driver"
⑦ 创建打印机 "VirtualPrinter" (绑定 VP_Port)
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
④ 停止 VirtualPrinterService
⑤ 删除打印机 "VirtualPrinter"
⑥ 根据勾选状态:
   - 勾选 "删除端口" → 删除端口 "VP_Port"
   - 勾选 "删除驱动" → 删除驱动 "Microsoft PS Class Driver"
⑦ 注销端口监视器 (DeleteMonitor)
⑧ 删除 VPPostScriptMon.dll
⑨ 删除 VirtualPrinterService
⑩ 删除安装目录
⑪ 卸载完成
```

### 2.3 打印文档

```
① 用户在应用程序 (Word/Excel/浏览器) 中:
   文件 → 打印 → 选择 "VirtualPrinter" → 打印
② Windows Spooler → PostScript 驱动 → 端口监视器捕获 PS 数据
③ 端口监视器写入临时文件 → 通知后台服务
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

1. `InitializePrintMonitor2` → 初始化监视器实例
2. `OpenPort` → 打开 "VirtualPrinterPort:" 端口
3. `StartDocPort` → 打印作业开始, 创建临时文件: `%TEMP%\VirtualPrinter\{JobId}.ps`
4. `WritePort` → 接收 PostScript 数据, 写入临时文件
5. `EndDocPort` → 打印作业结束, 关闭文件句柄, 通过 Named Pipe 通知后台服务
6. `ClosePort` → 关闭端口

#### 3.1.3 Named Pipe 通信协议

端口监视器向后台服务发送指令：

```
指令格式: JSON
{
  "Action": "NewJob",
  "JobId": 123,
  "TempFile": "C:\\Users\\...\\Temp\\VirtualPrinter\\123.ps",
  "DocumentName": "我的文档.docx",
  "PrinterName": "VirtualPrinter",
  "UserName": "username",
  "Timestamp": "2026-01-01T12:00:00"
}
```

### 3.2 VirtualPrinterService（后台处理服务）

**语言**: C# .NET 8  
**类型**: Windows Service + 前台辅助进程

#### 3.2.1 核心模块

| 模块 | 职责 |
|------|------|
| `PipeListener` | 监听 Named Pipe，接收端口监视器的通知 |
| `JobProcessor` | 管理打印作业队列，控制并发处理 |
| `GhostscriptBridge` | 调用 Ghostscript，生成目标格式 |
| `SaveDialogManager` | 弹出保存对话框（需要在用户会话中交互） |
| `ConfigManager` | 读写配置文件 |
| `FileNamingEngine` | 根据规则生成文件名 |
| `WatermarkEngine` | 水印叠加处理 (PostScript + Image layer) |
| `NotificationManager` | 打印完成通知 |

#### 3.2.2 工作流程

```
1. PipeListener 收到 NewJob 通知
2. JobProcessor 将任务加入队列
3. 启动 VirtualPrinterSaveDialog.exe 到用户 Session (CreateProcessAsUser)
4. 保存对话框以模态置顶窗口显示:
   - 用户选择输出格式 (PDF/PNG/JPEG/BMP/TIFF)
   - 选择保存路径和文件名
   - 设置分辨率/颜色模式/分页方式
   - [打印] → 配置发回服务 → 继续
   - [取消] → 清理临时文件 → 结束
5. GhostscriptBridge 调用 gswin64c.exe:
   - PDF:   gswin64c -sDEVICE=pdfwrite -dCompatibilityLevel=1.7 -o output.pdf input.ps
   - PNG:   gswin64c -sDEVICE=png16m -r300 -o output_%d.png input.ps
   - JPEG:  gswin64c -sDEVICE=jpeg -r300 -dJPEGQ=85 -o output_%d.jpg input.ps
   - BMP:   gswin64c -sDEVICE=bmp16m -r300 -o output_%d.bmp input.ps
   - TIFF:  gswin64c -sDEVICE=tiff24nc -r300 -o output.tiff input.ps
   (多页图片: %d 自动分页; 多页 TIFF: 合并为单文件)
6. 如启用水印: 调用 Ghostscript 叠加水印图层
7. 输出完成后清理临时文件 (.ps)
8. 打开输出文件夹 (可选)
```

#### 3.2.3 Session 0 隔离处理

Windows Service 运行在 Session 0，无法直接显示 UI。解决方案：

- **方案**: 服务无常驻 UI，收到打印任务时，使用 `CreateProcessAsUser` 在用户 Session 中启动 `VirtualPrinterSaveDialog.exe`
- 服务通过 Named Pipe `\\.\pipe\VirtualPrinter_{SessionId}` 与对话框通信
- 对话框以模态置顶窗口显示，用户配置后发回服务处理
- 对话框处理完毕后进程自动退出，不常驻系统托盘

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

**语言**: C# .NET 8 WPF  
**类型**: 打印机安装/卸载管理工具 (从开始菜单启动, 无常驻)  
**专注**: 只负责打印机安装/卸载和环境管理, 不包含打印输出设置

#### 3.3.1 功能模块

| 模块 | 功能 |
|------|------|
| `EnvDetector` | 系统环境检测 (.NET/VC++/Spooler/PostScript 驱动) |
| `PrinterInstaller` | 安装打印机 (释放文件 + 注册端口监视器 + 安装驱动) |
| `PrinterUninstaller` | 卸载打印机 (含端口/驱动选项勾选框) |
| `DefaultSettings` | 全局默认值 (默认保存目录、默认格式、水印默认文本) |
| `AboutPage` | 版本信息、帮助 |

#### 3.3.2 UI 界面布局

```
┌──────────────────────────────────────────────────────┐
│  VirtualPrinter 管理工具                   — □ ×    │
├──────────┬───────────────────────────────────────────┤
│          │  ┌─ 打印机状态 ──────────────────────┐   │
│ 📠 管理   │  │  📠 VirtualPrinter ● 已安装       │   │
│ ℹ 关于   │  │  端口: VP_Port                    │   │
│          │  │  驱动: Microsoft PS Class Driver   │   │
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
1. 检测系统版本 (Win7+)
2. 检测 .NET 8 Runtime，如缺失提示安装
3. 将 VPPostScriptMon.dll 复制到 %SYSTEMROOT%\System32\
4. 注册端口监视器 (AddMonitor API)
5. 使用 Windows 内置 "Microsoft PS Class Driver" 添加打印机
6. 创建端口 "VirtualPrinterPort:" 绑定到监视器
7. 安装 VirtualPrinterService Windows 服务
8. 启动服务
9. 复制 Ghostscript (gsdll64.dll, gswin64c.exe) 到安装目录
10. 创建开始菜单快捷方式
11. 注册 VirtualPrinterGUI 为启动项 (可选)
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

| 输出格式 | Ghostscript 设备 | 颜色模式 | 参数 |
|---------|-----------------|---------|------|
| PDF | `pdfwrite` | — | `-dPDFSETTINGS=/prepress -dCompatibilityLevel=1.7` |
| PNG | `png16m` | RGB 24bit | `-r{resolution} -dTextAlphaBits=4 -dGraphicsAlphaBits=4` |
| PNG 灰度 | `pnggray` | 灰度 8bit | `-r{resolution}` |
| PNG 黑白 | `pngmono` | 黑白 1bit | `-r{resolution}` |
| JPEG | `jpeg` | RGB 24bit | `-r{resolution} -dJPEGQ={quality} -dTextAlphaBits=4` |
| JPEG 灰度 | `jpeggray` | 灰度 8bit | `-r{resolution} -dJPEGQ={quality}` |
| BMP | `bmp16m` | RGB 24bit | `-r{resolution}` |
| BMP 灰度 | `bmpgray` | 灰度 8bit | `-r{resolution}` |
| BMP 黑白 | `bmpmono` | 黑白 1bit | `-r{resolution}` |
| TIFF | `tiff24nc` | RGB 24bit | `-r{resolution}` |
| TIFF 灰度 | `tiffgray` | 灰度 8bit | `-r{resolution}` |
| TIFF 黑白 | `tiffg4` | 黑白 1bit CCITT G4 | `-r{resolution}` |
| TIFF CMYK | `tiff32nc` | CMYK 32bit | `-r{resolution}` |

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

### 5.3 水印叠加方案

水印通过 Ghostscript 的 PostScript 图层叠加实现:
```
1. 首先生成无 PS 文件的最终输出
2. 生成水印 PS 文件 (包含水印文字的 PostScript 代码)
3. 使用 pdfwrite 将水印合并到原文件
   (或对图片使用 composite 方式叠加)

水印 PS 模板:
  /Helvetica-Bold findfont 48 scalefont setfont
  gsave
    0.8 setgray           % 透明度/灰度
    30 rotate             % 旋转角度
    100 500 moveto        % 位置
    (机密文件) show       % 水印文字
  grestore
```

对于图片格式 (PNG/JPEG/BMP/TIFF), 水印通过 System.Drawing 或 ImageSharp 在 GS 输出后叠加。

## 6. 项目文件结构

```
VirtualPrinter/
├── DESIGN.md                          # 本设计文档
├── README.md                          # 项目说明
│
├── src/
│   ├── VPPostScriptMon/               # 端口监视器 DLL (C++)
│   │   ├── CMakeLists.txt
│   │   ├── main.cpp                   # DLL 入口
│   │   ├── Monitor.cpp/.h             # Monitor 实现
│   │   ├── Port.cpp/.h                # Port 实现
│   │   ├── PipeClient.cpp/.h          # Named Pipe 客户端
│   │   └── resource.rc                # 版本资源
│   │
│   ├── VirtualPrinterService/         # 后台服务 (C#)
│   │   ├── VirtualPrinterService.csproj
│   │   ├── Program.cs                 # 服务入口
│   │   ├── Services/
│   │   │   ├── PipeListener.cs        # Named Pipe 监听
│   │   │   ├── JobProcessor.cs        # 作业队列处理
│   │   │   ├── GhostscriptBridge.cs   # GS 调用封装
│   │   │   └── ConfigManager.cs       # 配置管理
│   │   ├── Models/
│   │   │   ├── PrintJob.cs            # 打印作业模型
│   │   │   └── Settings.cs            # 配置模型
│   │   └── Utils/
│   │       ├── FileNamingEngine.cs    # 文件名规则引擎
│   │       └── TempFileManager.cs     # 临时文件管理
│   │
│   ├── VirtualPrinterManager/         # 打印机安装/卸载工具 (C# WPF)
│   │   ├── VirtualPrinterManager.csproj
│   │   ├── App.xaml
│   │   ├── MainWindow.xaml/.cs        # 主窗口 (环境检测 + 安装/卸载)
│   │   ├── ViewModels/
│   │   │   ├── MainViewModel.cs
│   │   │   └── AboutViewModel.cs
│   │   ├── Services/
│   │   │   ├── ServiceClient.cs       # 与后台服务通信
│   │   │   ├── PrinterInstaller.cs    # 打印机安装
│   │   │   ├── PrinterUninstaller.cs  # 打印机卸载
│   │   │   └── EnvChecker.cs          # 环境检测 (调用 EnvChecker.dll)
│   │
│   ├── VirtualPrinterSaveDialog/      # 保存对话框 (C# WPF)
│   │   ├── VirtualPrinterSaveDialog.csproj
│   │   ├── App.xaml
│   │   ├── MainWindow.xaml/.cs        # 保存对话框窗口
│   │   └── Services/
│   │       └── ServiceClient.cs       # 与服务通信
│   │
│   └── VPGhostLib/                    # Ghostscript 封装库 (C#)
│       ├── VPGhostLib.csproj
│       ├── GhostscriptAPI.cs          # 高级 API
│       ├── GSDevice.cs                # 设备枚举
│       ├── GSArguments.cs             # 参数构建
│       └── GSProcess.cs               # 进程管理
│
├── installer/
│   ├── setup.iss                      # Inno Setup 主脚本
│   ├── detection.iss                  # 环境检测脚本 (Inno Setup Pascal)
│   ├── prereq.iss                     # 依赖安装脚本
│   ├── components/
│   │   └── EnvChecker.dll             # 环境检测原生 DLL (C++, 由 setup 调用)
│   │       ├── checker.h              # 检测接口声明
│   │       ├── os_check.cpp           # OS 版本/架构检测
│   │       ├── dotnet_check.cpp       # .NET Framework 检测
│   │       ├── vc_check.cpp           # VC++ Redist 检测
│   │       ├── printer_check.cpp      # 打印机驱动检测
│   │       └── spooler_check.cpp      # Spooler 服务检测
│   ├── drivers/
│   │   └── ps/                        # PostScript 驱动包 (内嵌)
│   │       ├── msprint.inf            # Microsoft PS Class Driver INF
│   │       ├── msprint.ppd            # PostScript PPD
│   │       ├── ps5ui.dll              # PS UI DLL
│   │       ├── pscript5.dll           # PS 驱动 DLL
│   │       ├── pscript.ntf            # 字体映射文件
│   │       └── README.txt             # 驱动版本说明
│   └── assets/
│       ├── logo.bmp                   # 安装程序图标
│       ├── banner.bmp                 # 安装程序横幅
│       └── detection_progress.bmp     # 检测进度页面背景
│
├── lib/                               # 第三方依赖
│   └── README.md                      # 依赖说明 (Ghostscript 需要单独下载)
│
├── scripts/
│   ├── build_all.bat                  # 一键构建脚本
│   └── sign.bat                       # 代码签名 (可选)
│
└── docs/
    └── architecture.md                # 架构说明图
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
- PostScript 驱动在 Win7 上为 "Microsoft PS Class Driver"
- VC++ Redist 2015-2022 (Win7 不自带, 安装程序自动安装)
- 端口监视器 API 在 Win7-Win11 上一致
- 使用 Win32 API 的 subset 兼容所有版本

### 7.5 打印机驱动安装无需管理员

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

**EnvChecker.dll API**:
```c
// 检测 DLL 导出函数
HRESULT WINAPI CheckOSVersion(OUT OSVERSIONINFOEX* info);
HRESULT WINAPI CheckDotNetFramework(OUT DWORD* releaseVersion, OUT BOOL* installed);
HRESULT WINAPI CheckVCRedist(OUT BOOL* installed, OUT DWORD* versionMajor);
HRESULT WINAPI CheckPrintSpooler(OUT BOOL* running);
HRESULT WINAPI CheckPSDriver(OUT BOOL* installed);
HRESULT WINAPI CheckGhostscript(OUT BOOL* installed, OUT WCHAR* path[MAX_PATH]);
HRESULT WINAPI CheckDiskSpace(IN LPCWSTR path, IN DWORD64 requiredMB, OUT BOOL* sufficient);
HRESULT WINAPI GetArchitecture(OUT WCHAR* arch[16]);
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
│     ├─ .NET Framework 4.7.2+  [Win7需安装]             │
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
| .NET Framework | 注册表 `Release` 键值 | >= 461808 (4.7.2) | Win7: 静默安装 .NET 4.8 | 高 |
| VC++ Redist | 注册表 VC 运行库键 | 版本 >= 14.0 | 静默安装 vc_redist.x64.exe | 高 |
| Print Spooler | `QueryServiceStatus` | 服务运行中 | `StartService` 自动启动 | 高 |
| PS Class Driver | `PNPGetDevice` / `Get-PrinterDriver` | 已安装 | Win10+: 启用 Windows 功能; Win7: 从 cab 安装 | 高 |
| 端口监视器 DLL | `CheckFile` + `AddMonitor` 测试 | DLL 存在且可加载 | 复制 DLL + 注册 | 高 |
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

#### 8.3.3 PostScript 驱动自动安装

```
驱动包来源:
  从 Windows 系统提取并打包到安装包中:
  installer\drivers\ps\ 目录包含:
    ├── msprint.inf         # Microsoft PS Class Driver INF
    ├── msprint.ppd         # PostScript PPD 文件
    ├── ps5ui.dll           # PS UI DLL
    ├── pscript5.dll        # PS 驱动 DLL
    └── pscript.ntf         # 字体映射文件

检测逻辑:
  PowerShell: Get-PrinterDriver -Name "*PostScript*" | Where-Object { $_.Name -like "*Microsoft*PS*" }
  或 PnP 设备查询

安装方式 (全系统统一):
  # 使用 pnputil 添加驱动包到驱动存储区
  pnputil -a installer\drivers\ps\msprint.inf

  # 使用 printui.dll 添加打印机驱动
  rundll32 printui.dll,PrintUIEntry /ia /m "Microsoft PS Class Driver" /h "x64" /f "installer\drivers\ps\msprint.inf"

  # 创建端口
  rundll32 printui.dll,PrintUIEntry /if /b "VirtualPrinter" /f "installer\drivers\ps\msprint.inf" /r "VP_Port" /m "Microsoft PS Class Driver"

注意事项:
  - Win7 需要 KB3191566 (XPS 到 PS 转换支持)
  - Win10/11 1809+ 已内置, 但内嵌驱动确保离线安装能力
  - 驱动包从 Windows 10 22H2 SDK 提取, 兼容 Win7-Win11
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
| Ghostscript | 10.04.0+ | PS → PDF/Image 转换引擎 | 内嵌 (所有系统) | 注册表: `HKLM\SOFTWARE\GPL Ghostscript\` |
| .NET Framework | 4.7.2+ | C# 组件运行时 | Win10/11 内置; Win7 从 CDN 下载 | 注册表: `HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full\Release` |
| PostScript 驱动 | 系统组件 | 生成 PostScript 数据 | 内嵌 (全系统) | `Get-PrinterDriver` PowerShell 检测 |
| Print Spooler | 系统服务 | 打印队列管理 | 系统内置 | `Get-Service Spooler` |
| VC++ Redist | 2015-2022 x64 | C++ 端口监视器运行时 | Win10/11 内嵌; Win7 从 CDN 下载 | 注册表: `HKLM\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64` |
| Windows SDK | 10.0+ | 端口监视器开发 | 开发环境 | — |
| CMake | 3.20+ | C++ 项目构建 | 开发环境 | — |
| Visual Studio 2022 | 17.0+ | C++ 和 C# 编译 | 开发环境 | — |

## 10. 构建与发布

### 10.1 开发环境配置

```
1. 安装 Visual Studio 2022 (含 C++ 和 .NET 工作负载)
2. 安装 Windows SDK 10.0.20348.0+
3. 安装 CMake 3.20+
4. 下载 Ghostscript 10.04.0 发行版
5. 安装 Inno Setup 6.2+
```

### 10.2 构建脚本

```bash
# 构建所有组件
scripts\build_all.bat

# 构建端口监视器 (C++)
cd src\VPPostScriptMon
cmake -B build -A x64
cmake --build build --config Release

# 构建 C# 组件
dotnet build src\VirtualPrinterService -c Release
dotnet build src\VirtualPrinterGUI -c Release
dotnet build src\VirtualPrinterSaveDialog -c Release
dotnet build src\VPGhostLib -c Release

# 构建安装包
iscc installer\setup.iss
```

### 10.3 输出文件

构建完成后，安装包输出到 `dist/VirtualPrinter_Setup.exe`。

## 11. 测试策略

| 测试类型 | 范围 | 工具/方法 |
|---------|------|----------|
| 单元测试 | C# 组件 | xUnit |
| 组件测试 | 端口监视器 | 模拟 Spooler 调用 |
| 集成测试 | 完整打印流程 | 从应用程序打印到输出文件 |
| 兼容性测试 | Win7/Win10/Win11 | 虚拟机测试 |
| 性能测试 | 大文件/多并发 | 100MB PS 文件测试 |
| 格式测试 | 所有输出格式 | 验证 PDF/PNG/JPEG/BMP/TIFF |

---

## 附录 A: 端口监视器 API 参考

```c
// 注册/注销端口监视器
BOOL AddMonitor(LPWSTR pName, DWORD Level, LPBYTE pMonitor);
BOOL DeleteMonitor(LPWSTR pName, LPWSTR pEnvironment, LPWSTR pMonitorName);

// MONITOR_INFO_2 结构
typedef struct _MONITOR_INFO_2 {
    LPWSTR pName;         // 监视器名称
    LPWSTR pEnvironment;  // "Windows x64"
    LPWSTR pDLLName;      // "VPPostScriptMon.dll"
} MONITOR_INFO_2;

// MONITOR2 函数表 (端口监视器必须实现的接口)
typedef struct _MONITOR2 {
    DWORD cbSize;
    BOOL (*pfnEnumPorts)(...);
    BOOL (*pfnOpenPort)(...);
    BOOL (*pfnStartDocPort)(...);
    BOOL (*pfnWritePort)(...);
    BOOL (*pfnReadPort)(...);
    BOOL (*pfnEndDocPort)(...);
    BOOL (*pfnClosePort)(...);
    BOOL (*pfnAddPort)(...);
    BOOL (*pfnAddPortUI)(...);
    BOOL (*pfnConfigurePortUI)(...);
    BOOL (*pfnDeletePort)(...);
    BOOL (*pfnDeletePortUI)(...);
    BOOL (*pfnXcvOpenPort)(...);
    BOOL (*pfnXcvDataPort)(...);
    BOOL (*pfnXcvClosePort)(...);
    BOOL (*pfnShutdownMonitor)(...);
} MONITOR2;
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
