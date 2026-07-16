#include "Monitor.h"
#include "Port.h"
#include <string>
#include <vector>
#include <mutex>
#include <unordered_map>
#include <cstdarg>
#include <wchar.h>
#include <chrono>
#include <stdio.h>

static long long GetTickUs() {
    auto now = std::chrono::steady_clock::now();
    return std::chrono::duration_cast<std::chrono::microseconds>(now.time_since_epoch()).count();
}

static void LogToFile(const wchar_t* msg) {
    FILE* f = NULL;
    if (_wfopen_s(&f, L"C:\\Temp\\VPPSMon.log", L"a") == 0 && f) {
        fwprintf(f, L"%s", msg);
        fclose(f);
    }
}

void DbgTrace(const wchar_t* fmt, ...) {
    wchar_t buf[1024];
    long long us = GetTickUs();
    int prefix = swprintf_s(buf, 1024, L"[VPPSMon %lld.%06lld] ", us / 1000000, us % 1000000);
    if (prefix < 0) prefix = 0;
    va_list args;
    va_start(args, fmt);
    vswprintf_s(buf + prefix, 1024 - prefix, fmt, args);
    va_end(args);
    size_t len = wcsnlen_s(buf, 1024);
    if (len + 1 < 1024) { buf[len] = L'\n'; buf[len + 1] = L'\0'; }
    OutputDebugStringW(buf);
    LogToFile(buf);
}

static std::mutex g_monitorLock;
static std::unordered_map<HANDLE, PortContext*> g_ports;

static const WCHAR VP_MONITOR_NAME[] = L"VirtualPrinter Port Monitor";
static const WCHAR MONITORS_REG_KEY[] = L"SYSTEM\\CurrentControlSet\\Control\\Print\\Monitors\\VirtualPrinter Port Monitor\\Ports";

static DWORD GetStrSize(const WCHAR* s) {
    return (DWORD)(wcslen(s) + 1) * sizeof(WCHAR);
}

static std::vector<std::wstring> ReadPortsFromRegistry() {
    std::vector<std::wstring> ports;
    HKEY hKey = NULL;
    if (RegOpenKeyExW(HKEY_LOCAL_MACHINE, MONITORS_REG_KEY, 0, KEY_READ, &hKey) == ERROR_SUCCESS) {
        DWORD index = 0;
        WCHAR valueName[256];
        DWORD valueNameSize = 256;
        DWORD valueType = 0;
        BYTE valueData[256];
        DWORD valueDataSize = 256;
        while (RegEnumValueW(hKey, index, valueName, &valueNameSize, NULL, &valueType, valueData, &valueDataSize) == ERROR_SUCCESS) {
            if (valueType == REG_SZ || valueType == REG_MULTI_SZ) {
                ports.push_back(valueName);
            }
            index++;
            valueNameSize = 256;
            valueDataSize = 256;
        }
        RegCloseKey(hKey);
    }
    if (ports.empty()) {
        ports.push_back(L"VP_Port");
    }
    return ports;
}

static BOOL WINAPI EnumPorts(
    HANDLE hMonitor, LPWSTR pName, DWORD Level, LPBYTE pPorts,
    DWORD cbBuf, LPDWORD pcbNeeded, LPDWORD pcReturned)
{
    DbgTrace(L"EnumPorts called Level=%d cbBuf=%d", Level, cbBuf);
    (void)hMonitor; (void)pName;

    if (Level != 1 && Level != 2) {
        *pcbNeeded = 0; *pcReturned = 0;
        DbgTrace(L"EnumPorts Level=%d not supported, returning 0", Level);
        return TRUE;
    }

    std::vector<std::wstring> portNames = ReadPortsFromRegistry();
    DWORD count = (DWORD)portNames.size();

    DWORD structSize = (Level == 1) ? sizeof(PORT_INFO_1W) : sizeof(PORT_INFO_2W);
    DWORD strTotal = 0;
    for (auto& name : portNames) {
        strTotal += GetStrSize(name.c_str());
        if (Level == 2) {
            strTotal += GetStrSize(VP_MONITOR_NAME);
            strTotal += GetStrSize(L"VirtualPrinter Port");
        }
    }

    DWORD needed = structSize * count + strTotal;
    *pcbNeeded = needed;

    if (cbBuf < structSize) {
        *pcReturned = 0;
        DbgTrace(L"EnumPorts: cbBuf=%d too small for 1 struct (need %d), returning 0 ports", cbBuf, structSize);
        return TRUE;
    }

    DWORD maxFit = count > 0 ? cbBuf / (needed / count) : 1;
    if (maxFit > count) maxFit = count;
    if (maxFit < 1) { *pcReturned = 0; DbgTrace(L"EnumPorts: 0 fit, returning 0 ports"); return TRUE; }

    DWORD actualCount = count < maxFit ? count : maxFit;
    LPBYTE pEnd = pPorts + cbBuf;
    LPBYTE pStruct = pPorts;

    if (Level == 1) {
        PPORT_INFO_1W pInfo = (PPORT_INFO_1W)pStruct;
        for (DWORD i = 0; i < actualCount; i++) {
            DWORD sz = GetStrSize(portNames[i].c_str());
            pEnd -= sz;
            wcscpy_s((LPWSTR)pEnd, sz / sizeof(WCHAR), portNames[i].c_str());
            pInfo[i].pName = (LPWSTR)pEnd;
        }
    }
    else if (Level == 2) {
        PPORT_INFO_2W pInfo = (PPORT_INFO_2W)pStruct;
        for (DWORD i = 0; i < actualCount; i++) {
            DWORD szName = GetStrSize(portNames[i].c_str());
            pEnd -= szName;
            wcscpy_s((LPWSTR)pEnd, szName / sizeof(WCHAR), portNames[i].c_str());
            pInfo[i].pPortName = (LPWSTR)pEnd;

            DWORD szMon = GetStrSize(VP_MONITOR_NAME);
            pEnd -= szMon;
            wcscpy_s((LPWSTR)pEnd, szMon / sizeof(WCHAR), VP_MONITOR_NAME);
            pInfo[i].pMonitorName = (LPWSTR)pEnd;

            DWORD szDesc = GetStrSize(L"VirtualPrinter Port");
            pEnd -= szDesc;
            wcscpy_s((LPWSTR)pEnd, szDesc / sizeof(WCHAR), L"VirtualPrinter Port");
            pInfo[i].pDescription = (LPWSTR)pEnd;

            pInfo[i].fPortType = PORT_TYPE_WRITE;
            pInfo[i].Reserved = 0;
        }
    }

    DbgTrace(L"EnumPorts returning %d port(s) (Level=%d), needed=%d cbBuf=%d", actualCount, Level, needed, cbBuf);
    *pcReturned = actualCount;
    return TRUE;
}

static BOOL WINAPI OpenPort(HANDLE hMonitor, LPWSTR pszName, PHANDLE phPort)
{
    long long t0 = GetTickUs();
    DbgTrace(L"OpenPort ENTER port=%s", pszName ? pszName : L"(null)");
    (void)hMonitor;
    if (!pszName || !phPort) { DbgTrace(L"OpenPort: invalid args elapsed=%lldus", GetTickUs() - t0); return FALSE; }

    PortContext* ctx = PortContext::Create(pszName);
    if (!ctx) { DbgTrace(L"OpenPort: Create failed elapsed=%lldus", GetTickUs() - t0); return FALSE; }

    std::lock_guard<std::mutex> lock(g_monitorLock);
    g_ports[(HANDLE)ctx] = ctx;
    *phPort = (HANDLE)ctx;
    DbgTrace(L"OpenPort OK port=%s handle=%p elapsed=%lldus", pszName, (void*)ctx, GetTickUs() - t0);
    return TRUE;
}

static BOOL WINAPI OpenPortEx(HANDLE hMonitor, HANDLE hMonitorPort, LPWSTR pPortName, LPWSTR pPrinterName, PHANDLE phPort, struct _MONITOR2 FAR* pMonitor2)
{
    return OpenPort(hMonitor, pPortName, phPort);
}

static BOOL WINAPI StartDocPort(
    HANDLE hPort, LPWSTR pszPrinterName, DWORD JobId,
    DWORD Level, LPBYTE pDocInfo)
{
    long long t0 = GetTickUs();
    DbgTrace(L"StartDocPort ENTER handle=%p printer=%s JobId=%d", hPort, pszPrinterName ? pszPrinterName : L"(null)", JobId);
    (void)Level;
    std::lock_guard<std::mutex> lock(g_monitorLock);
    auto it = g_ports.find(hPort);
    if (it == g_ports.end()) { SetLastError(ERROR_INVALID_HANDLE); DbgTrace(L"StartDocPort: handle not found elapsed=%lldus", GetTickUs() - t0); return FALSE; }
    BOOL ok = it->second->StartDoc(pszPrinterName, JobId, (PDOC_INFO_1W)pDocInfo);
    DbgTrace(L"StartDocPort: returning %d elapsed=%lldus", ok, GetTickUs() - t0);
    return ok;
}

static BOOL WINAPI WritePort(
    HANDLE hPort, LPBYTE pBuffer, DWORD cbBuf, LPDWORD pcbWritten)
{
    long long t0 = GetTickUs();
    DbgTrace(L"WritePort ENTER handle=%p cbBuf=%u", hPort, cbBuf);
    std::lock_guard<std::mutex> lock(g_monitorLock);
    auto it = g_ports.find(hPort);
    if (it == g_ports.end()) { SetLastError(ERROR_INVALID_HANDLE); DbgTrace(L"WritePort: handle not found elapsed=%lldus", GetTickUs() - t0); return FALSE; }
    BOOL ok = it->second->Write(pBuffer, cbBuf, pcbWritten);
    DbgTrace(L"WritePort handle=%p ok=%d written=%u elapsed=%lldus", hPort, ok, pcbWritten ? *pcbWritten : 0, GetTickUs() - t0);
    return ok;
}

static BOOL WINAPI ReadPort(
    HANDLE hPort, LPBYTE pBuffer, DWORD cbBuffer, LPDWORD pcbRead)
{
    (void)hPort; (void)pBuffer; (void)cbBuffer;
    *pcbRead = 0;
    SetLastError(ERROR_CALL_NOT_IMPLEMENTED);
    return FALSE;
}

static BOOL WINAPI EndDocPort(HANDLE hPort)
{
    long long t0 = GetTickUs();
    DbgTrace(L"EndDocPort ENTER handle=%p", hPort);
    std::lock_guard<std::mutex> lock(g_monitorLock);
    auto it = g_ports.find(hPort);
    if (it == g_ports.end()) { SetLastError(ERROR_INVALID_HANDLE); DbgTrace(L"EndDocPort: handle not found elapsed=%lldus", GetTickUs() - t0); return FALSE; }
    BOOL ok = it->second->EndDoc();
    DbgTrace(L"EndDocPort: returning %d elapsed=%lldus", ok, GetTickUs() - t0);
    return ok;
}

static BOOL WINAPI ClosePort(HANDLE hPort)
{
    DbgTrace(L"ClosePort called handle=%p", hPort);
    std::lock_guard<std::mutex> lock(g_monitorLock);
    auto it = g_ports.find(hPort);
    if (it == g_ports.end()) { DbgTrace(L"ClosePort: handle not found"); return FALSE; }
    it->second->Close();
    delete it->second;
    g_ports.erase(it);
    DbgTrace(L"ClosePort OK");
    return TRUE;
}

static BOOL WINAPI AddPort(HANDLE hMonitor, LPWSTR pName, HWND hWnd, LPWSTR pMonitorName)
{
    (void)hMonitor; (void)pName; (void)hWnd; (void)pMonitorName;
    return TRUE;
}

static BOOL WINAPI AddPortEx(HANDLE hMonitor, LPWSTR pName, DWORD Level, LPBYTE lpBuffer, LPWSTR lpMonitorName)
{
    (void)hMonitor; (void)pName; (void)Level; (void)lpBuffer; (void)lpMonitorName;
    return TRUE;
}

static BOOL WINAPI ConfigurePort(HANDLE hMonitor, LPWSTR pName, HWND hWnd, LPWSTR pPortName)
{
    (void)hMonitor; (void)pName; (void)hWnd; (void)pPortName;
    return TRUE;
}

static BOOL WINAPI DeletePort(HANDLE hMonitor, LPWSTR pName, HWND hWnd, LPWSTR pPortName)
{
    (void)hMonitor; (void)pName; (void)hWnd; (void)pPortName;
    return TRUE;
}

static BOOL WINAPI GetPrinterDataFromPort(
    HANDLE hPort, DWORD ControlID, LPWSTR pValueName, LPWSTR lpInBuffer,
    DWORD cbInBuffer, LPWSTR lpOutBuffer, DWORD cbOutBuffer, LPDWORD lpcbReturned)
{
    (void)hPort; (void)ControlID; (void)pValueName; (void)lpInBuffer;
    (void)cbInBuffer; (void)lpOutBuffer; (void)cbOutBuffer;
    *lpcbReturned = 0;
    SetLastError(ERROR_CALL_NOT_IMPLEMENTED);
    return FALSE;
}

static BOOL WINAPI SetPortTimeOuts(HANDLE hPort, LPCOMMTIMEOUTS lpCTO, DWORD reserved)
{
    (void)hPort; (void)lpCTO; (void)reserved;
    return TRUE;
}

static BOOL WINAPI XcvOpenPort(HANDLE hMonitor, LPCWSTR pszObject, ACCESS_MASK GrantedAccess, PHANDLE phXcv)
{
    (void)hMonitor; (void)pszObject; (void)GrantedAccess;
    *phXcv = (HANDLE)1;
    return TRUE;
}

static void AddPortToRegistry(LPCWSTR portName) {
    HKEY hKey = NULL;
    if (RegCreateKeyExW(HKEY_LOCAL_MACHINE, MONITORS_REG_KEY, 0, NULL, 0, KEY_WRITE, NULL, &hKey, NULL) == ERROR_SUCCESS) {
        RegSetValueExW(hKey, portName, 0, REG_SZ, (BYTE*)L"", (DWORD)sizeof(WCHAR));
        RegCloseKey(hKey);
    }
}

static void DeletePortFromRegistry(LPCWSTR portName) {
    HKEY hKey = NULL;
    if (RegOpenKeyExW(HKEY_LOCAL_MACHINE, MONITORS_REG_KEY, 0, KEY_WRITE, &hKey) == ERROR_SUCCESS) {
        RegDeleteValueW(hKey, portName);
        RegCloseKey(hKey);
    }
}

static DWORD WINAPI XcvDataPort(
    HANDLE hXcv, LPCWSTR pszDataName, LPBYTE pInputData,
    DWORD cbInputData, LPBYTE pOutputData, DWORD cbOutputData,
    LPDWORD pcbOutputNeeded)
{
    (void)hXcv; (void)pInputData; (void)cbInputData;

    if (!pszDataName)
    {
        *pcbOutputNeeded = 0;
        return ERROR_SUCCESS;
    }

    DbgTrace(L"XcvDataPort query=%s", pszDataName);

    if (wcscmp(pszDataName, L"Monitor") == 0)
    {
        // Return monitor name so spooler can identify this port's monitor
        DWORD needed = (DWORD)((wcslen(VP_MONITOR_NAME) + 1) * sizeof(WCHAR));
        *pcbOutputNeeded = needed;
        if (cbOutputData < needed)
            return ERROR_INSUFFICIENT_BUFFER;
        CopyMemory(pOutputData, VP_MONITOR_NAME, needed);
        DbgTrace(L"XcvDataPort Monitor -> %s", VP_MONITOR_NAME);
        return ERROR_SUCCESS;
    }

    if (wcscmp(pszDataName, L"Port") == 0)
    {
        auto ports = ReadPortsFromRegistry();
        if (ports.empty()) {
            *pcbOutputNeeded = 0;
            return ERROR_SUCCESS;
        }
        DWORD needed = GetStrSize(ports[0].c_str());
        *pcbOutputNeeded = needed;
        if (cbOutputData < needed)
            return ERROR_INSUFFICIENT_BUFFER;
        CopyMemory(pOutputData, ports[0].c_str(), needed);
        DbgTrace(L"XcvDataPort Port -> %s", ports[0].c_str());
        return ERROR_SUCCESS;
    }

    if (wcscmp(pszDataName, L"PortExists") == 0)
    {
        BOOL exists = FALSE;
        if (pInputData && cbInputData > 0) {
            auto ports = ReadPortsFromRegistry();
            for (auto& name : ports) {
                if (_wcsicmp((LPCWSTR)pInputData, name.c_str()) == 0) {
                    exists = TRUE;
                    break;
                }
            }
        }
        *pcbOutputNeeded = sizeof(BOOL);
        if (cbOutputData < sizeof(BOOL))
            return ERROR_INSUFFICIENT_BUFFER;
        *(BOOL*)pOutputData = exists;
        DbgTrace(L"XcvDataPort PortExists -> %d", exists);
        return ERROR_SUCCESS;
    }

    if (wcscmp(pszDataName, L"AddPort") == 0)
    {
        if (pInputData && cbInputData >= (int)sizeof(WCHAR))
        {
            LPCWSTR portName = (LPCWSTR)pInputData;
            DbgTrace(L"XcvDataPort AddPort port=%s", portName);
            AddPortToRegistry(portName);
        }
        if (pcbOutputNeeded) *pcbOutputNeeded = 0;
        return ERROR_SUCCESS;
    }

    if (wcscmp(pszDataName, L"DeletePort") == 0)
    {
        if (pInputData && cbInputData >= (int)sizeof(WCHAR))
        {
            LPCWSTR portName = (LPCWSTR)pInputData;
            DbgTrace(L"XcvDataPort DeletePort port=%s", portName);
            DeletePortFromRegistry(portName);
        }
        if (pcbOutputNeeded) *pcbOutputNeeded = 0;
        return ERROR_SUCCESS;
    }

    if (cbOutputData > 0 && pOutputData)
        ZeroMemory(pOutputData, cbOutputData);
    *pcbOutputNeeded = 0;
    return ERROR_SUCCESS;
}

static BOOL WINAPI XcvClosePort(HANDLE hXcv)
{
    (void)hXcv;
    return TRUE;
}

static VOID WINAPI Shutdown(HANDLE hMonitor)
{
    DbgTrace(L"Shutdown called");
    (void)hMonitor;
    std::lock_guard<std::mutex> lock(g_monitorLock);
    for (auto& pair : g_ports) {
        pair.second->Close();
        delete pair.second;
    }
    g_ports.clear();
}

static DWORD WINAPI SendRecvBidiDataFromPort(HANDLE hPort, DWORD dwAccessBit, LPCWSTR pAction, PVOID pReqData, PVOID* ppResData)
{
    DbgTrace(L"SendRecvBidiDataFromPort action=%s", pAction ? pAction : L"(null)");
    (void)hPort; (void)dwAccessBit; (void)pAction; (void)pReqData;
    *ppResData = NULL;
    // Bidi not supported; return ERROR_NOT_SUPPORTED so spooler doesn't expect further bidi
    return ERROR_NOT_SUPPORTED;
}

static DWORD WINAPI NotifyUsedPorts(HANDLE hMonitor, DWORD cPorts, PCWSTR* ppszPorts)
{
    (void)hMonitor; (void)cPorts; (void)ppszPorts;
    return ERROR_SUCCESS;
}

static DWORD WINAPI NotifyUnusedPorts(HANDLE hMonitor, DWORD cPorts, PCWSTR* ppszPorts)
{
    (void)hMonitor; (void)cPorts; (void)ppszPorts;
    return ERROR_SUCCESS;
}

static DWORD WINAPI PowerEvent(HANDLE hMonitor, DWORD event, PVOID pSettings)
{
    (void)hMonitor; (void)event; (void)pSettings;
    return ERROR_SUCCESS;
}

PMONITOR2 WINAPI GetMonitor2(void)
{
    static MONITOR2 mon = { 0 };
    static bool initialized = false;
    if (!initialized) {
        mon.cbSize = sizeof(MONITOR2);
        mon.pfnEnumPorts = EnumPorts;
        mon.pfnOpenPort = OpenPort;
        mon.pfnOpenPortEx = OpenPortEx;
        mon.pfnStartDocPort = StartDocPort;
        mon.pfnWritePort = WritePort;
        mon.pfnReadPort = ReadPort;
        mon.pfnEndDocPort = EndDocPort;
        mon.pfnClosePort = ClosePort;
        mon.pfnAddPort = AddPort;
        mon.pfnAddPortEx = AddPortEx;
        mon.pfnConfigurePort = ConfigurePort;
        mon.pfnDeletePort = DeletePort;
        mon.pfnGetPrinterDataFromPort = GetPrinterDataFromPort;
        mon.pfnSetPortTimeOuts = SetPortTimeOuts;
        mon.pfnXcvOpenPort = XcvOpenPort;
        mon.pfnXcvDataPort = XcvDataPort;
        mon.pfnXcvClosePort = XcvClosePort;
        mon.pfnShutdown = Shutdown;
        mon.pfnSendRecvBidiDataFromPort = SendRecvBidiDataFromPort;
        mon.pfnNotifyUsedPorts = NotifyUsedPorts;
        mon.pfnNotifyUnusedPorts = NotifyUnusedPorts;
        mon.pfnPowerEvent = PowerEvent;
        initialized = true;
    }
    return &mon;
}

static BOOL WINAPI AddPortUI(PCWSTR pszServer, HWND hWnd, PCWSTR pszMonitorNameIn, PWSTR* ppszPortNameOut)
{
    (void)pszServer; (void)hWnd; (void)pszMonitorNameIn; (void)ppszPortNameOut;
    return TRUE;
}

static BOOL WINAPI ConfigurePortUI(PCWSTR pName, HWND hWnd, PCWSTR pPortName)
{
    (void)pName; (void)hWnd; (void)pPortName;
    return TRUE;
}

static BOOL WINAPI DeletePortUI(PCWSTR pszServer, HWND hWnd, PCWSTR pszPortName)
{
    (void)pszServer; (void)hWnd; (void)pszPortName;
    return TRUE;
}

PMONITORUI WINAPI GetMonitorUI(void)
{
    static MONITORUI ui = { 0 };
    static bool initialized = false;
    if (!initialized) {
        ui.dwMonitorUISize = sizeof(MONITORUI);
        ui.pfnAddPortUI = AddPortUI;
        ui.pfnConfigurePortUI = ConfigurePortUI;
        ui.pfnDeletePortUI = DeletePortUI;
        initialized = true;
    }
    return &ui;
}
