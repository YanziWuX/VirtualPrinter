# VirtualPrinter 技术文档

## 项目概述

**VirtualPrinter** 是一款 Windows 虚拟打印机驱动程序，可在不依赖微软驱动签名认证的情况下，拦截任意应用程序的打印作业，捕获 PostScript 数据并转换为 **PDF、PNG、JPEG、BMP、TIFF** 格式。支持 Windows 7 SP1 / 10 / 11（x64）。

**核心业务价值**：
- 解决传统虚拟打印机需要昂贵的驱动签名认证的问题
- 提供灵活的格式转换选项，支持水印、分辨率、色彩模式等自定义
- 作为 Windows 服务运行，支持跨 Session 0 隔离的用户界面交互

---

## 总体架构

### 系统架构图

```
[应用程序 (Word/Chrome/Excel)]
    │ File → Print → "YanziWu PDF-IMG Printer"
    ▼
[Windows Print Spooler (spoolsv.exe)]
    │ PostScript Driver (Canon Generic Plus PS3)
    ▼
[VPPostScriptMon.dll] ──── 写入临时文件 ────→ %TEMP%\VirtualPrinter\{JobId}.tmp
    │
    │ EndDocPort: 启动独立线程
    ▼
[NamedPipe] \\.\pipe\VirtualPrinter
    │ JSON: {"Action":"NewJob","JobId":N,"TempFile":"...","DocumentName":"..."}
    ▼
[VirtualPrinterService] (Session 0, LocalSystem)
    ├─ PipeListener → 接收作业通知
    ├─ JobProcessor → 作业队列 & 调度
    ├─ ResultPipeListener → 接收结果回传
    └─ SessionLauncher → 跨 Session 启动 SaveDialog
    │
    │ CreateProcessAsUser(winsta0\default)
    ▼
[VirtualPrinterSaveDialog.exe] (用户 Session)
    ├─ GSConvert → 调用 Ghostscript 进行格式转换
    ├─ WatermarkEngine → 图片/PDF 水印叠加
    └─ ServiceClient → 通过 NamedPipe 回传结果
    │
    ▼
[NamedPipe] \\.\pipe\VirtualPrinterResult
    │ JSON: {"Action":"JobResult","JobId":N,"OutputPath":"...","Success":true/false}
    ▼
[VirtualPrinterService] → 完成 JobResult TaskCompletionSource → 日志记录
```

### 项目结构

```
VirtualPrinter.sln
├── src/
│   ├── VPPostScriptMon/          # C++ DLL - 打印监控器 (注入 spoolsv.exe)
│   ├── VPGhostLib/               # C# 类库 - Ghostscript 封装核心
│   ├── VirtualPrinterService/    # C# Windows 服务 - 中央调度器
│   ├── VirtualPrinterSaveDialog/ # C# WPF - 打印保存对话框
│   ├── VirtualPrinterManager/    # C# WPF - 打印机安装/卸载管理器
│   ├── VirtualPrinterLauncher/   # C# 控制台 - 自解压启动器 (便携模式)
│   ├── VirtualPrinter.Tests/     # C# xUnit 单元测试
│   └── RedmonRedirector/         # C++ EXE - REDMON 兼容重定向器
├── assets/                       # 资源文件
├── installer/                    # 安装程序
├── lib/                          # 第三方库 (GS, VC++ Redist)
├── build.bat                     # 一键构建脚本
├── DESIGN.md                     # 设计文档
└── README.md                     # 自述文件
```

### 目标平台 & 框架

| 项目 | 语言 | 类型 | 目标框架 |
|------|------|------|----------|
| VPPostScriptMon | C++ | DLL (Port Monitor) | Win32/x64 |
| VPGhostLib | C# | Class Library | .NET Framework 4.8 |
| VirtualPrinterService | C# | Windows Service EXE | .NET Framework 4.8 |
| VirtualPrinterSaveDialog | C# | WPF EXE | .NET Framework 4.8 |
| VirtualPrinterManager | C# | WPF EXE | .NET Framework 4.8 |
| VirtualPrinterLauncher | C# | Console EXE | .NET Framework 4.8 |
| RedmonRedirector | C++ | EXE | Win32/x64 |
| VirtualPrinter.Tests / GsTest | C# | xUnit / Console | .NET Framework 4.8 |
| TestPipeline | C# | WinForms EXE | .NET 9.0 |

---

## 核心模块详解

### 1. VPPostScriptMon (C++ Port Monitor DLL)

**职责**：作为 Windows 打印监视器注入到 `spoolsv.exe` 中，接收打印机驱动输出的 PostScript 数据流，写入临时文件并通过命名管道通知服务。

**关键类**：

| 类/文件 | 描述 |
|---------|------|
| `DllMain.cpp` | DLL 入口，导出 `InitializeMonitor` / `InitializePrintMonitor2` / `InitializePrintMonitorUI` |
| `Monitor.h / Monitor.cpp` | `MONITOR2` 结构体实现（22 个函数指针），注册表端口枚举 |
| `Port.h / Port.cpp` | `PortContext` 类：`StartDoc` → `Write` → `EndDoc` 生命周期管理 |
| `PipeClient.h / PipeClient.cpp` | 命名管道客户端：通知 `VirtualPrinterService` |

**核心流程**：

```
OpenPort → StartDocPort(创建 .tmp 文件) → WritePort(写入 PS 数据) → EndDocPort(关闭文件，启动通知线程) → ClosePort
```

**关键细节**：
- `EndDocPort()` 使用 `std::thread`（detached）调用 `NotifyService`，避免阻塞后台打印程序
- 端口存储在注册表 `HKLM\...\Monitors\VirtualPrinter Port Monitor\Ports`
- 默认端口名称：`VP_Port`

---

### 2. VPGhostLib (C# Ghostscript 封装库)

**职责**：零外部依赖的 Ghostscript 命令行封装，提供统一的格式转换接口。

**命名空间**：`VirtualPrinter.GhostLib`

#### GSConvert

格式转换引擎，无外部依赖（纯 .NET Framework 4.8）。

| 方法 | 签名 | 返回值 | 描述 |
|------|------|--------|------|
| `Convert` | `(string inputPs, string outputPath, GSConvertOptions opts)` | `GSConvertResult` | 执行 GS 进程转换，处理输出验证和图片拼接 |
| `FindGSExecutable` | `()` | `string` | 搜索 9 个已知路径查找 `gswin64c.exe` |
| `GetDeviceName` | `(OutputFormat fmt, ColorMode color)` | `string` | 将格式+色彩模式映射到 GS 设备名 |
| `BuildArguments` | `(GSConvertOptions opts, string inputPs, string outputPath)` | `string` | 构建完整 GS 命令行参数 |
| `StitchImages` | `(List<string> pageFiles, string outputPath, OutputFormat fmt)` | `void` | 多页图片拼接为一张长图 |

#### GSConvertOptions — 转换选项

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `Format` | `OutputFormat` 枚举 | `PDF` | 输出格式 |
| `ColorMode` | `ColorMode` 枚举 | `RGB` | 色彩模式 |
| `Resolution` | `int` | `300` | DPI |
| `JpegQuality` | `int` | `85` | JPEG 质量 (1-100) |
| `EmbedFonts` | `bool` | `true` | PDF 嵌入字体 |
| `PdfVersion` | `string` | `"1.7"` | PDF 兼容版本 |
| `MultiPageTiff` | `bool` | `true` | 多页合并为单一 TIFF |
| `TimeoutSeconds` | `int` | `120` | GS 进程超时 |
| `GsPath` | `string` | `null` | 自定义 GS 路径 |
| `MergeImagePages` | `bool` | `false` | 拼接所有页为一张长图 |
| `RasterizeToPdf` | `bool` | `false` | 先转图片再构建 PDF（无外部依赖） |

#### GSConvertResult — 转换结果

| 属性 | 类型 | 说明 |
|------|------|------|
| `Success` | `bool` | 转换成功 |
| `OutputPath` | `string` | 输出文件路径 |
| `ErrorMessage` | `string` | 失败时的错误描述 |
| `ExitCode` | `int` | Ghostscript 进程退出码 |

#### 枚举

```csharp
OutputFormat { PDF, PNG, JPEG, BMP, TIFF }
ColorMode { RGB, Grayscale, CMYK, BlackWhite }
```

**GS 设备映射矩阵**（21 种组合，含 CMYK JPEG / mono JPEG）：

| 格式 | RGB | Grayscale | CMYK | BlackWhite |
|------|-----|-----------|------|------------|
| PDF | `pdfwrite` | `pdfwrite` | `pdfwrite` | `pdfwrite` |
| PNG | `png16m` | `pnggray` | `png16m` | `pngmono` |
| JPEG | `jpeg` | `jpeggray` | `jpegcmyk` | `jpegmono` |
| BMP | `bmp16m` | `bmpgray` | `bmp16m` | `bmpmono` |
| TIFF | `tiff24nc` | `tiffgray` | `tiff32nc` | `tiffg4` |

**超时处理**：默认 120 秒，超时后强制终止 GS 进程。

---

### 3. VirtualPrinterService (C# Windows 服务)

**职责**：中央调度服务，运行在 Session 0，协调作业接收、调度、跨 Session UI 启动和结果回传。

#### MainService — 服务主入口

| 方法 | 描述 |
|------|------|
| `OnStart(string[] args)` | 初始化所有子组件：ConfigManager, JobProcessor, PipeListener, ResultPipeListener, TempFileManager, FileSystemWatcher |
| `OnStop()` | 取消所有 Token，停止监听，清理资源 |
| `OnJobResult(JobResult result)` | 将结果转发给 JobProcessor，释放作业闸门锁 |
| `OnOutputFileChanged(object, FileSystemEventArgs)` | 文件监控处理器(1500ms 防抖)，创建 PrintJob 并入队 |

#### PipeListener — 作业通知管道服务器

| 方法 | 描述 |
|------|------|
| `Start(CancellationToken token)` | 在后台任务启动监听循环 |
| `Stop()` | 取消监听 |
| `ListenLoop` (私有异步) | 接受 `\\.\pipe\VirtualPrinter` 连接 |
| `ProcessConnectionAsync` (私有异步) | 读取 Unicode JSON，调用 `_processor.Enqueue(json)` |

**编码**：Unicode (UTF-16LE)

#### ResultPipeListener — 结果回传管道服务器

| 方法 | 描述 |
|------|------|
| `Start(CancellationToken token)` | 启动监听 |
| `Stop()` | 取消监听 |
| `ListenLoop` (私有异步) | 接受 `\\.\pipe\VirtualPrinterResult` 连接 |
| `ProcessConnectionAsync` (私有异步) | 读取 UTF-8 JSON，反序列化为 `JobResult`，触发回调 |

**编码**：UTF-8（与 PipeListener 不同）

#### JobProcessor — 作业队列 & 调度核心

| 方法 | 描述 |
|------|------|
| `Enqueue(string json)` | 解析 JSON 为 `PrintJob` 并入队 |
| `Enqueue(PrintJob job)` | 直接入队 `ConcurrentQueue<PrintJob>` |
| `OnJobResult(JobResult result)` | 通过 `TaskCompletionSource<JobResult>` 完成挂起的作业 |
| `ProcessLoop` (私有异步) | 后台循环出队并启动 `ProcessJob` |
| `ProcessJob` (私有异步) | 启动 SaveDialog，12 分钟超时等待结果 |
| `LaunchSaveDialog` (私有) | 先尝试 `SessionLauncher.LaunchInActiveSession`，失败则回退 `Process.Start` |

**关键数据结构**：
- `ConcurrentQueue<PrintJob> _queue` — 线程安全作业队列
- `ConcurrentDictionary<int, TaskCompletionSource<JobResult>> _pendingJobs` — 作业 ID 到异步结果的映射

#### SessionLauncher — 跨 Session 0 启动器

使用 P/Invoke 将 SaveDialog 进程从 Session 0（服务）提升到用户桌面 Session：

```
WTSQueryUserToken → DuplicateTokenEx → CreateEnvironmentBlock → CreateProcessAsUser(winsta0\default)
```

**P/Invoke 声明**：`kernel32.dll`, `wtsapi32.dll`, `advapi32.dll`, `userenv.dll`

#### ConfigManager — 配置管理

- 配置路径：`%APPDATA%\VirtualPrinter\settings.json`
- 序列化方式：`System.Web.Script.Serialization.JavaScriptSerializer`
- 默认输出目录：`%USERPROFILE%\Documents\VirtualPrinter`

#### TempFileManager — 临时文件清理

- 扫描目录：`%ProgramData%\VirtualPrinter` 和 `%ProgramData%\VirtualPrinter\jobs`
- 文件过期时间：15 分钟
- 清理周期：5 分钟（`System.Timers.Timer`）
- 匹配模式：`*.ps`, `*.pending`, `*.tmp`

#### FileNamingEngine — 文件名生成引擎

| 方法 | 描述 |
|------|------|
| `GenerateFileName(string rule, string documentName, int jobId, string format)` | 替换 Token 生成最终文件名 |
| `SanitizeFileName(string name)` (私有) | 替换非法字符为 `_`，去除尾部点号，空值回退为 `"output"` |

**支持 Token**：`{DocumentName}`, `{Date}`, `{Time}`, `{DateTime}`, `{JobId}`, `{Format}`
（注意：代码中不支持 `{PrinterName}` 和 `{UserName}`，与 DESIGN.md 附录 B 不同）

---

### 4. VirtualPrinterSaveDialog (C# WPF 保存对话框)

**职责**：在用户 Session 中显示打印选项对话框，执行格式转换和水印处理，回传结果。

#### MainWindow — 主窗口

| 方法 | 描述 |
|------|------|
| `OnLoaded` | 解析命令行参数，加载配置，启动 10 分钟自动关闭计时器 |
| `OnPrint` (异步) | 核心：.pending → .ps → GSConvert 转换 → 水印 → 结果回传 → 清理退出 |
| `OnCancel` | 发送取消结果，清理，退出 |
| `OnBrowseFolder` | 弹出文件夹选择对话框 |
| `OnAdvancedSettings` | 打开高级设置对话框 |
| `LoadSettings` / `SaveSettings` | 读写 `save_dialog_settings.json`（自定义 JSON 解析） |

**多页处理状态机**：

```
PDF 模式 → PDF 合并选项（单选），隐藏图片选项
图像格式+合并勾选+TIFF → MultiPageTiff=true（单文件多页 TIFF）
图像格式+合并勾选+其他 → MergeImagePages=true（拼接长图）
图像格式+不合并 → 每页单独文件
```

**进程退出三阶段保证**：
1. 优雅 WPF `Close()`
2. `Application.Current.Shutdown()`
3. `Environment.Exit(0)`

#### WatermarkEngine — 水印引擎

| 方法 | 描述 |
|------|------|
| `Apply(string filePath, string text, string position, int opacity)` | 根据扩展名自动派发到图片或 PDF 水印 |
| `ApplyImageWatermark` (私有静态) | 使用 `System.Drawing.Graphics.DrawString` 叠加图片水印，支持阿尔法透明度 |
| `ApplyPdfWatermark` (私有静态) | 生成 Ghostscript PostScript 水印层，通过 `pdfwrite` 设备合并 |
| `GetPositionBounds` (私有静态) | 返回 6 个位置的 RectangleF |

**水印不透明度**：`alpha = opacity * 255 / 100`，灰度色 RGB(128,128,128)

**PDF 水印技术**：使用 GS 的 `setpagedevice` 配合 `EndPage` 过程，在每页上覆盖文字：

```postscript
<< /EndPage {
    2 eq { pop false } {
        gsave /Helvetica-Bold 36 selectfont alpha setgray pos moveto (text) show grestore true
    } ifelse
} >> setpagedevice
```

**6 种水印位置**：Center, TopLeft, TopRight, BottomLeft, BottomRight, Tile

#### ServiceClient — 结果回传客户端

| 方法 | 描述 |
|------|------|
| `SendResultAsync(int jobId, string outputPath, bool success, string error)` | 连接 `\\.\pipe\VirtualPrinterResult`，发送 JSON 结果（连接超时 300ms，写入超时 500ms） |

---

### 5. VirtualPrinterManager (C# WPF 管理器)

**职责**：打印机安装、卸载和环境检测。

**安装流程**：
1. 管理员权限检查
2. 创建目录结构
3. 添加打印机端口
4. 安装 Windows 服务
5. 创建打印机实例
6. 失败时自动回滚

**环境检测项**：
- .NET Framework 版本（注册表 `Release` 键）
- Visual C++ Redistributable（注册表）
- 后台打印程序运行状态（ServiceController）
- PostScript 驱动安装状态（WMI）
- Ghostscript 存在性（文件系统）

**打印机常量**：
- 打印机名称：`"YanziWu PDF-IMG Printer"`
- 驱动：`"Canon Generic Plus PS3"`
- 端口：`"VP_Port"`（VirtualPrinter 监视器端口，通过注册表 `HKLM\...\Monitors\VirtualPrinter Port Monitor\Ports\VP_Port` 管理）
- 端口监控 DLL：`VPPostScriptMon.dll` 复制到 `%SYSTEMROOT%\System32\`
- 日志路径：`%ALLUSERSPROFILE%\VirtualPrinter\install.log`
- 输出目录：`%ProgramData%\VirtualPrinter\`
- 作业监控目录：`%ProgramData%\VirtualPrinter\jobs\`

**端口监视器注册表结构**：
```
HKLM\SYSTEM\CurrentControlSet\Control\Print\Monitors\VirtualPrinter Port Monitor\
  Driver = "VPPostScriptMon.dll"  (REG_SZ)
  Ports\
    VP_Port = ""                  (REG_SZ, 值名即端口名)
```

**XcvDataPort 支持的操作**（Monitor.cpp 实现）：
| 查询名 | 功能 | 输入 | 输出 |
|--------|------|------|------|
| `Monitor` | 返回监视器名称 | — | `"VirtualPrinter Port Monitor"` |
| `Port` | 返回第一个端口名 | — | `"VP_Port"` |
| `PortExists` | 检查端口是否存在 | 端口名字符串 | `BOOL` |
| `AddPort` | 添加端口到注册表 | 端口名字符串 | — |
| `DeletePort` | 从注册表删除端口 | 端口名字符串 | — |

---

### 6. VirtualPrinterLauncher (C# 控制台自解压启动器)

**职责**：便携模式入口，将嵌入的 `bundle.zip` 解压到 `%ALLUSERSPROFILE%\VirtualPrinter`，请求管理员权限后启动 Manager。

**处理锁定文件**：如果 `VirtualPrinterManager.exe` 被占用，先重命名为 `.old` 再解压。

---

## 命名管道协议

| 管道名 | 方向 | 编码 | 客户端 | 服务端 | 报文格式 |
|--------|------|------|--------|--------|----------|
| `\\.\pipe\VirtualPrinter` | → In | **Unicode (UTF-16LE)** | VPPostScriptMon / RedmonRedirector | PipeListener (Service) | `{"Action":"NewJob","JobId":N,"TempFile":"...","DocumentName":"...","PrinterName":"..."}` |
| `\\.\pipe\VirtualPrinterResult` | → In | **UTF-8** | ServiceClient (SaveDialog) | ResultPipeListener (Service) | `{"Action":"JobResult","JobId":N,"OutputPath":"...","Success":true/false,"Error":"..."}` |

> **注意**: 两个管道使用不同的编码。PipeListener 用 `StreamReader(pipe, Encoding.Unicode)` 读取 UTF-16LE，ResultPipeListener 用 `StreamReader(pipe, Encoding.UTF8)` 读取 UTF-8。

---

## 配置体系

## 配置体系

| 文件 | 路径 | 管理方式 |
|------|------|----------|
| `settings.json` | `%APPDATA%\VirtualPrinter\` | `ConfigManager` (JavaScriptSerializer) |
| `save_dialog_settings.json` | `%APPDATA%\VirtualPrinter\` | `MainWindow` (自定义 JSON 解析) |
| `install.log` | `%ALLUSERSPROFILE%\VirtualPrinter\` | Manager 安装日志 |
| 临时作业文件 | `%TEMP%\VirtualPrinter\` | TempFileManager 定期清理 |
| 文件监控目录 | `C:\Temp\VPPrint\jobs\` | FileSystemWatcher + TempFileManager |
| 注册表端口 | `HKLM\...\Monitors\VirtualPrinter Port Monitor\Ports` | Monitor.cpp 枚举/管理 |

---

## 关键算法与复杂逻辑

### 1. Session 0 隔离突破
服务运行在 Session 0，无法直接显示 UI。`SessionLauncher` 通过 `WTSQueryUserToken` 获取活动用户的令牌，配合 `DuplicateTokenEx` 和 `CreateProcessAsUser`，在用户桌面 Session 启动 SaveDialog。回退方案使用常规 `Process.Start`。

### 2. GS 命令行参数构建
`GSConvert.BuildArguments` 根据 `OutputFormat` + `ColorMode` 选择对应 GS 设备（21 种组合），附加格式专属参数：
- PDF：`-dCompatibilityLevel=1.7 -dEmbedAllFonts=true -dPDFSETTINGS=/prepress`
- JPEG：`-dJPEGQ={quality}`
- TIFF（黑白）：`-dDownScaleColorImages=false -dDownScaleGrayImages=false`
- 图像分页：`-sOutputFile="path_%d.ext"`（%d 由 GS 自动替换为页码）
- GS 搜索：按 9 个已知路径搜索 `gswin64c.exe`（Program Files、Program Files (x86)、本地 gs/ 子目录等）
- 超时：默认 120 秒，超时后强制终止 GS 进程

### 3. 多页图片拼接
`StitchImages` 将 Ghostscript 输出的多张单页图片合并为一张长图：
1. 计算所有页面的总高度
2. 创建合并位图
3. 逐页使用 `Graphics.DrawImage` 绘制
4. 使用对应格式的编码器保存

### 4. 文件写入防抖
`MainService` 使用 `FileSystemWatcher` 监控 `output.prn`，结合 1500ms 防抖计时器和 `_jobInProgress` 闸门锁，防止在文件写入未完成时触发处理。

### 5. 水印 PDF 叠加
区别于简单图片水印，PDF 水印利用 Ghostscript 的 `setpagedevice` 在页面渲染层面叠加文字，保留 PDF 的矢量特性。

### 6. Rasterize-to-PDF 模式
当 `GSConvertOptions.RasterizeToPdf` 为 true 时，启用两步渲染：
1. Ghostscript 将 PostScript 渲染为逐页 PNG 图片（临时目录）
2. 纯 C# PDF 构建器 (`CreatePdfFromImages`) 使用原生 PDF 语法构建 PDF 1.4 文件：
   - 每页 PNG 转为 JPEG 压缩嵌入
   - 构建 PDF 对象树（页面对象、流对象、资源字典）
   - 生成交叉引用表和文件尾
   - 不依赖任何外部 PDF 库

此模式在处理复杂 PostScript 文件时提供更好的渲染保真度。

### 7. 图片拼接 (StitchImages)
当选择"合并为单文件"且输出为图片格式（非 TIFF）时：
1. 计算所有页面总高度
2. 创建合并位图
3. 逐页使用 `Graphics.DrawImage` 绘制
4. 使用对应格式编码器保存

### 8. 启动静默期
服务启动后前 15 秒内的打印作业被静默丢弃，`JobProcessor` 自动清理临时文件。原因：后台处理程序可能在服务重启时重新提交历史作业，避免弹出大量废弃对话框。

---

## 外部依赖

| 依赖 | 版本 | 用途 |
|------|------|------|
| Ghostscript | 10.04.0 / 10.05.1 | PostScript → 目标格式转换（代码搜索 9 个路径，支持 10.04.0 和 10.05.1） |
| Canon Generic Plus PS3 | V3.0 (内嵌) | PostScript 3 仿真驱动，由 pnputil + printui.dll 安装 |
| Visual C++ Redist | 2015-2022 | VPPostScriptMon / EnvChecker |
| CMake | ≥ 3.10 | C++ 项目构建 |
| 7-Zip | 任意 | 构建时打包 bundle.zip |

> **注意**：所有生产项目均无 NuGet 外部依赖。JSON 序列化使用 `System.Web.Script.Serialization.JavaScriptSerializer`（.NET Framework 内置）或自定义的 `JsonParseFlat` 解析器（SaveDialog 的 `save_dialog_settings.json` 使用自定义 JSON 解析，避免引入依赖）。测试项目 `GsTest` 和 `TestPipeline` 也无外部 NuGet 包。

---

## 设计模式总结

| 模式 | 应用位置 |
|------|----------|
| 生产者-消费者 | PortMonitor → PipeListener → JobProcessor → GSConvert |
| 异步结果 (TaskCompletionSource) | JobProcessor: 等待 SaveDialog 返回结果 |
| 策略模式 | GSConvert: 格式+色彩 → 设备名映射 |
| 模板方法 | FileNamingEngine: 可替换 Token 的文件名生成 |
| 观察者模式 | FileSystemWatcher 监控文件变化 |
| 门锁模式 (Gate Lock) | `_jobInProgress` 防止并发文件监控触发 |

---

## 构建系统

`build.bat`（项目根目录，194 行，非 scripts/）八步构建流程：

```
1. Build EnvChecker (C++ CMake)
2. Build VPPostScriptMon (C++ CMake)
3. Build VPGhostLib (dotnet build)
4. 生成 VersionInfo.cs（含时间戳批次）
5. Build Service + SaveDialog + Manager (dotnet build)
6. 收集输出到 dist/bin/
7. 创建 bundle.zip (7-Zip) → 嵌入 Launcher 作为资源
8. Build VirtualPrinterLauncher (dotnet build)
```

**前置条件**：Visual Studio 2022（C++ 工具链）、.NET SDK（支持 net48）、CMake 3.10+、7-Zip、Ghostscript 10.04.0、Inno Setup 6.2+

---

## 测试覆盖

| 测试文件 | 目标 | 框架 |
|---------|------|------|
| `tests/GsTest/Program.cs` (700 行) | GS 转换管道（convert/pipe/compare/rasterize/devices/batch） | 自制测试台 |
| `tests/TestPipeline/Program.cs` (145 行) | 端到端 PS → PDF/PNG/JPEG 管道 | .NET 9.0 WinForms |
| `tests/addport.cs` (58 行) | XcvData 端口添加 Win32 API 测试 | 自制 |
| PowerShell 脚本 (`tests/`) | 服务安装/重启/日志/清理/部署 | PowerShell |
