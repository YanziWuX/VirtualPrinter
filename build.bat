@echo off
setlocal enabledelayedexpansion

set ROOT=%~dp0
set BUILD_DIR=%ROOT%\build
set DIST_DIR=%ROOT%\dist

echo ========================================
echo  VirtualPrinter Build Script
echo ========================================
echo.

REM Find CMake
set CMAKE=cmake.exe
where %CMAKE% >nul 2>&1
if errorlevel 1 (
    if exist "C:\Program Files\CMake\bin\cmake.exe" (
        set "CMAKE=C:\Program Files\CMake\bin\cmake.exe"
    ) else if exist "%ProgramFiles%\CMake\bin\cmake.exe" (
        set "CMAKE=%ProgramFiles%\CMake\bin\cmake.exe"
    ) else (
        echo Error: CMake not found in PATH or common install locations
        exit /b 1
    )
)

if "%VSWHERE%"=="" set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"

if not exist "%VSWHERE%" (
    echo Error: Visual Studio not found
    exit /b 1
)

for /f "usebackq delims=" %%i in (`"%VSWHERE%" -latest -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do (
    set VS_PATH=%%i
)

if "%VS_PATH%"=="" (
    echo Error: Visual Studio C++ tools not found
    exit /b 1
)

call "%VS_PATH%\VC\Auxiliary\Build\vcvars64.bat" >nul 2>&1

echo [1/6] Building EnvChecker (C++ Environment Checker)...
mkdir "%BUILD_DIR%\EnvChecker" 2>nul
cd /d "%BUILD_DIR%\EnvChecker"
"%CMAKE%" "%ROOT%\installer\components" -A x64 -DCMAKE_BUILD_TYPE=Release
cmake --build . --config Release
if errorlevel 1 (
    echo ERROR: EnvChecker build failed
    exit /b 1
)
echo   OK

echo [2/6] Building VPGhostLib...
cd /d "%ROOT%"
dotnet build src\VPGhostLib -c Release
if errorlevel 1 (
    echo ERROR: VPGhostLib build failed
    exit /b 1
)
echo   OK

echo [3/6] Building C# Applications...
REM Clean stale outputs from both possible paths
for %%P in (VPGhostLib VirtualPrinterService VirtualPrinterSaveDialog VirtualPrinterManager) do (
    if exist "%ROOT%\src\%%P\bin\Release\net48" rd /S /Q "%ROOT%\src\%%P\bin\Release\net48" 2>nul
    if exist "%ROOT%\src\%%P\bin\x64\Release\net48" rd /S /Q "%ROOT%\src\%%P\bin\x64\Release\net48" 2>nul
)
dotnet build src\VirtualPrinterService -c Release
if errorlevel 1 (
    echo WARNING: Service build had issues
)
dotnet build src\VirtualPrinterSaveDialog -c Release
if errorlevel 1 (
    echo WARNING: SaveDialog build had issues
)
dotnet build src\VirtualPrinterManager -c Release
if errorlevel 1 (
    echo WARNING: Manager build had issues
)
echo   OK

echo [4/6] Collecting output files...
mkdir "%DIST_DIR%" 2>nul
mkdir "%DIST_DIR%\bin" 2>nul

REM Copy C++ DLLs
copy /Y "%BUILD_DIR%\EnvChecker\Release\EnvChecker.dll" "%DIST_DIR%\bin\" >nul

REM Copy C# binaries (excluding launcher)
for %%P in (VirtualPrinterService VirtualPrinterSaveDialog VirtualPrinterManager) do (
    if exist "%ROOT%\src\%%P\bin\x64\Release\net48\*" (
        xcopy /E /I /Y "%ROOT%\src\%%P\bin\x64\Release\net48\*" "%DIST_DIR%\bin\" >nul
    ) else if exist "%ROOT%\src\%%P\bin\Release\net48\*" (
        xcopy /E /I /Y "%ROOT%\src\%%P\bin\Release\net48\*" "%DIST_DIR%\bin\" >nul
    )
)

REM Copy PostScript driver bundle
if exist "%ROOT%\src\VirtualPrinterManager\drivers\ps\vpvps.inf" (
    mkdir "%DIST_DIR%\bin\drivers\ps" 2>nul
    xcopy /E /I /Y "%ROOT%\src\VirtualPrinterManager\drivers\ps\*" "%DIST_DIR%\bin\drivers\ps\" >nul
    echo   PS driver bundle copied
) else (
    echo   WARNING: PS driver bundle not found
)

REM Copy Ghostscript files
if exist "%ROOT%\lib\gs\gswin64c.exe" (
    xcopy /E /I /Y "%ROOT%\lib\gs\*" "%DIST_DIR%\bin\gs\" >nul
    echo   Ghostscript files copied
) else (
    echo   WARNING: Ghostscript not found in lib\gs
)

REM Copy VC++ Redist (embedded for Win10/11 deployment)
if exist "%ROOT%\lib\vc_redist.x64.exe" (
    copy /Y "%ROOT%\lib\vc_redist.x64.exe" "%DIST_DIR%\bin\" >nul
    echo   VC++ Redist copied
) else (
    echo   WARNING: vc_redist.x64.exe not found in lib
)

echo   OK

echo [5/6] Building runtime bundle (ZIP)...
cd /d "%ROOT%"
REM Clean previous bundle
if exist "%ROOT%\src\VirtualPrinterLauncher\Resources\bundle.zip" del "%ROOT%\src\VirtualPrinterLauncher\Resources\bundle.zip"
REM Create ZIP using 7z (standard ZIP format readable by .NET)
"C:\Program Files\7-Zip\7z.exe" a -tzip -mx9 "%ROOT%\src\VirtualPrinterLauncher\Resources\bundle.zip" "%ROOT%\dist\bin\*" >nul
if errorlevel 1 (
    echo WARNING: Bundle creation failed, continuing
)
for %%F in ("%ROOT%\src\VirtualPrinterLauncher\Resources\bundle.zip") do echo   Bundle: %%~zF bytes
echo   OK

echo [6/6] Building VirtualPrinterLauncher (self-extracting)...
cd /d "%ROOT%"
REM Force rebuild by removing old output
if exist "%ROOT%\src\VirtualPrinterLauncher\bin\x64\Release\net48\VirtualPrinterLauncher.exe" del "%ROOT%\src\VirtualPrinterLauncher\bin\x64\Release\net48\VirtualPrinterLauncher.exe"
if exist "%ROOT%\src\VirtualPrinterLauncher\bin\Release\net48\VirtualPrinterLauncher.exe" del "%ROOT%\src\VirtualPrinterLauncher\bin\Release\net48\VirtualPrinterLauncher.exe"
dotnet build src\VirtualPrinterLauncher -c Release
if errorlevel 1 (
    echo ERROR: Launcher build failed
    exit /b 1
)
REM Find the actual output (may be bin\Release or bin\x64\Release depending on platform config)
if exist "%ROOT%\src\VirtualPrinterLauncher\bin\x64\Release\net48\VirtualPrinterLauncher.exe" (
    copy /Y "%ROOT%\src\VirtualPrinterLauncher\bin\x64\Release\net48\VirtualPrinterLauncher.exe" "%DIST_DIR%\VirtualPrinterLauncher.exe" >nul
) else if exist "%ROOT%\src\VirtualPrinterLauncher\bin\Release\net48\VirtualPrinterLauncher.exe" (
    copy /Y "%ROOT%\src\VirtualPrinterLauncher\bin\Release\net48\VirtualPrinterLauncher.exe" "%DIST_DIR%\VirtualPrinterLauncher.exe" >nul
)
for %%F in ("%DIST_DIR%\VirtualPrinterLauncher.exe") do echo   Output: %%F (%%~zF bytes)

echo.
echo ========================================
echo  Build Complete!
echo ========================================
echo.

endlocal
