#pragma once
#include <windows.h>

#ifdef ENVCHECKER_EXPORTS
#define ENVCHECKER_API __declspec(dllexport)
#else
#define ENVCHECKER_API __declspec(dllimport)
#endif

extern "C" {

ENVCHECKER_API HRESULT WINAPI CheckOSVersion(DWORD* major, DWORD* minor, DWORD* build, BOOL* isServer);
ENVCHECKER_API HRESULT WINAPI CheckArchitecture(WCHAR* arch, DWORD archSize);
ENVCHECKER_API HRESULT WINAPI IsAdmin(BOOL* admin);
ENVCHECKER_API HRESULT WINAPI CheckDotNetFramework(DWORD* releaseVersion, BOOL* installed);
ENVCHECKER_API HRESULT WINAPI CheckVCRedist(BOOL* installed, DWORD* versionMajor);
ENVCHECKER_API HRESULT WINAPI CheckPrintSpooler(BOOL* running);
ENVCHECKER_API HRESULT WINAPI CheckDiskSpace(LPCWSTR path, DWORD64 requiredMB, BOOL* sufficient);

}
