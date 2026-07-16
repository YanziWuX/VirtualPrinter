# 第三方依赖

本仓库不包含构建工具和运行时二进制的文件，以下说明如何准备构建环境。

## 构建工具 (系统安装)

| 工具 | 版本 | 用途 |
|------|------|------|
| Visual Studio 2022 | 17.x | 编译 .NET / C++ 项目 |
| .NET SDK | 8.0+ | `dotnet build` 编译 |
| CMake | 3.20+ | C++ 项目 (RedmonRedirector, VPPostScriptMon) |
| Inno Setup | 6.x | 生成安装包 (`iscc`) |
| Windows SDK | 10.0.x | `Inf2Cat`, `makecat`, `signtool` |

> 运行 `build.bat` 前确保上述工具均在 PATH 中。

## 运行时二进制 (放入 `lib/`)

### Ghostscript (必需)

下载 GPL Ghostscript 10.04.0 (x64):
- https://github.com/ArtifexSoftware/ghostpdl-downloads/releases

将以下文件放入 `lib/gs/`:
- `gswin64c.exe`
- `gsdll64.dll`
- (以及其他 GS 运行时文件)

### VC++ Redist (可选, 推荐)

下载 Visual C++ Redistributable for Visual Studio 2015-2022 (x64):
- https://aka.ms/vs/17/release/vc_redist.x64.exe

放入 `lib/vc_redist.x64.exe`

## 驱动程序依赖

打印机驱动使用 Canon Generic Plus PS3，已内嵌于 `drivers/CanonGenericPlusPS3/`，无需额外下载。
