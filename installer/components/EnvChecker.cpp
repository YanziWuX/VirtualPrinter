#define ENVCHECKER_EXPORTS
#include "EnvChecker.h"
#include <versionhelpers.h>

#pragma comment(lib, "advapi32.lib")

HRESULT WINAPI CheckOSVersion(DWORD* major, DWORD* minor, DWORD* build, BOOL* isServer)
{
    if (!major || !minor || !build || !isServer)
        return E_POINTER;

    HMODULE hMod = GetModuleHandleW(L"ntdll.dll");
    if (!hMod) return E_FAIL;

    typedef NTSTATUS(WINAPI* RtlGetVersionPtr)(PRTL_OSVERSIONINFOW);
    auto RtlGetVersion = (RtlGetVersionPtr)GetProcAddress(hMod, "RtlGetVersion");
    if (!RtlGetVersion) return E_FAIL;

    RTL_OSVERSIONINFOW ver = { sizeof(ver) };
    if (RtlGetVersion(&ver) != 0) return E_FAIL;

    *major = ver.dwMajorVersion;
    *minor = ver.dwMinorVersion;
    *build = ver.dwBuildNumber;

    *isServer = IsWindowsServer();
    return S_OK;
}

HRESULT WINAPI CheckArchitecture(WCHAR* arch, DWORD archSize)
{
    if (!arch) return E_POINTER;

    SYSTEM_INFO si;
    GetNativeSystemInfo(&si);

    switch (si.wProcessorArchitecture)
    {
    case PROCESSOR_ARCHITECTURE_AMD64:
        wcscpy_s(arch, archSize, L"x64");
        break;
    case PROCESSOR_ARCHITECTURE_INTEL:
        wcscpy_s(arch, archSize, L"x86");
        break;
    case PROCESSOR_ARCHITECTURE_ARM64:
        wcscpy_s(arch, archSize, L"ARM64");
        break;
    default:
        wcscpy_s(arch, archSize, L"Unknown");
        break;
    }
    return S_OK;
}

HRESULT WINAPI IsAdmin(BOOL* admin)
{
    if (!admin) return E_POINTER;

    PSID adminGroup = NULL;
    SID_IDENTIFIER_AUTHORITY ntAuthority = SECURITY_NT_AUTHORITY;

    if (!AllocateAndInitializeSid(&ntAuthority, 2, SECURITY_BUILTIN_DOMAIN_RID,
        DOMAIN_ALIAS_RID_ADMINS, 0, 0, 0, 0, 0, 0, &adminGroup))
        return HRESULT_FROM_WIN32(GetLastError());

    if (!CheckTokenMembership(NULL, adminGroup, admin))
        *admin = FALSE;

    FreeSid(adminGroup);
    return S_OK;
}

HRESULT WINAPI CheckDotNetFramework(DWORD* releaseVersion, BOOL* installed)
{
    if (!releaseVersion || !installed)
        return E_POINTER;

    *installed = FALSE;
    *releaseVersion = 0;

    HKEY hKey;
    if (RegOpenKeyExW(HKEY_LOCAL_MACHINE,
        L"SOFTWARE\\Microsoft\\NET Framework Setup\\NDP\\v4\\Full",
        0, KEY_READ, &hKey) == ERROR_SUCCESS)
    {
        DWORD value = 0, size = sizeof(value);
        if (RegQueryValueExW(hKey, L"Release", NULL, NULL,
            (LPBYTE)&value, &size) == ERROR_SUCCESS)
        {
            *releaseVersion = value;
            *installed = (value >= 461808); // 4.7.2+
        }
        RegCloseKey(hKey);
    }
    return S_OK;
}

HRESULT WINAPI CheckVCRedist(BOOL* installed, DWORD* versionMajor)
{
    if (!installed || !versionMajor)
        return E_POINTER;

    *installed = FALSE;
    *versionMajor = 0;

    HKEY hKey;
    if (RegOpenKeyExW(HKEY_LOCAL_MACHINE,
        L"SOFTWARE\\Microsoft\\VisualStudio\\14.0\\VC\\Runtimes\\x64",
        0, KEY_READ, &hKey) == ERROR_SUCCESS)
    {
        DWORD value = 0, size = sizeof(value);
        if (RegQueryValueExW(hKey, L"Major", NULL, NULL,
            (LPBYTE)&value, &size) == ERROR_SUCCESS)
        {
            *versionMajor = value;
            *installed = (value >= 14);
        }
        RegCloseKey(hKey);
    }
    return S_OK;
}

HRESULT WINAPI CheckPrintSpooler(BOOL* running)
{
    if (!running) return E_POINTER;
    *running = FALSE;

    SC_HANDLE scm = OpenSCManagerW(NULL, NULL, SC_MANAGER_CONNECT);
    if (!scm) return HRESULT_FROM_WIN32(GetLastError());

    SC_HANDLE service = OpenServiceW(scm, L"Spooler", SERVICE_QUERY_STATUS);
    if (service)
    {
        SERVICE_STATUS status;
        if (QueryServiceStatus(service, &status))
            *running = (status.dwCurrentState == SERVICE_RUNNING);
        CloseServiceHandle(service);
    }
    CloseServiceHandle(scm);
    return S_OK;
}

HRESULT WINAPI CheckDiskSpace(LPCWSTR path, DWORD64 requiredMB, BOOL* sufficient)
{
    if (!path || !sufficient) return E_POINTER;

    ULARGE_INTEGER freeBytes;
    if (GetDiskFreeSpaceExW(path, &freeBytes, NULL, NULL))
    {
        *sufficient = (freeBytes.QuadPart >= (requiredMB * 1024 * 1024));
    }
    else
    {
        *sufficient = FALSE;
    }
    return S_OK;
}
