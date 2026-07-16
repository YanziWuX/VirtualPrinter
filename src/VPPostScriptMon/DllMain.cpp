#include <windows.h>
#include <cstdarg>
#include <wchar.h>
#include "Monitor.h"

static void DbgTrace(const wchar_t* fmt, ...) {
    wchar_t buf[512];
    va_list args;
    va_start(args, fmt);
    vswprintf_s(buf, 512, fmt, args);
    va_end(args);
    size_t len = wcsnlen_s(buf, 512);
    if (len + 1 < 512) { buf[len] = L'\n'; buf[len + 1] = L'\0'; }
    OutputDebugStringW(L"[VPPSMon] ");
    OutputDebugStringW(buf);
}

BOOL WINAPI DllMain(HINSTANCE hinstDLL, DWORD fdwReason, LPVOID lpvReserved)
{
    (void)hinstDLL;
    (void)lpvReserved;
    if (fdwReason == DLL_PROCESS_ATTACH)
        DisableThreadLibraryCalls(hinstDLL);
    if (fdwReason == DLL_PROCESS_DETACH)
        DbgTrace(L"DLL_PROCESS_DETACH");
    return TRUE;
}

extern "C"
{

__declspec(dllexport) BOOL WINAPI InitializeMonitor(LPWSTR pszRegistryRoot)
{
    DbgTrace(L"InitializeMonitor called pszRegistryRoot=%s", pszRegistryRoot ? pszRegistryRoot : L"(null)");
    (void)pszRegistryRoot;
    return TRUE;
}

__declspec(dllexport) PMONITOR2 WINAPI InitializePrintMonitor2(PMONITORINIT pMonitorInit, PHANDLE phMonitor)
{
    DbgTrace(L"InitializePrintMonitor2 called");
    (void)pMonitorInit;
    *phMonitor = (HANDLE)1;
    return GetMonitor2();
}

__declspec(dllexport) PMONITORUI WINAPI InitializePrintMonitorUI(VOID)
{
    DbgTrace(L"InitializePrintMonitorUI called");
    return GetMonitorUI();
}

}
