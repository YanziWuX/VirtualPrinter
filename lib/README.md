# 第三方依赖

构建前需要将以下依赖放入对应目录：

## Ghostscript (必需)

下载 GPL Ghostscript 10.04.0 (x64):
- https://github.com/ArtifexSoftware/ghostpdl-downloads/releases

将以下文件放入 `lib/gs/`:
- gswin64c.exe
- gsdll64.dll
- (以及其他 GS 运行时文件)

## VC++ Redist (可选, 推荐)

下载 Visual C++ Redistributable for Visual Studio 2015-2022 (x64):
- https://aka.ms/vs/17/release/vc_redist.x64.exe

放入 `lib/vc_redist.x64.exe`

## PostScript 驱动包 (必需)

从 Windows 10 SDK 或系统提取 Microsoft PS Class Driver 文件放入:
- `installer/drivers/ps/`

需要的文件:
- msprint.inf
- msprint.ppd
- ps5ui.dll
- pscript5.dll
- pscript.ntf

也可以从已安装的 Windows 系统复制:
1. 打开 `printui.dll,PrintUIEntry /Xs /n "任意PS打印机"`
2. 或从 `C:\Windows\System32\DriverStore\FileRepository\` 查找
