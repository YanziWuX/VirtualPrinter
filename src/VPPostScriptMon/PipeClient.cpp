#include "PipeClient.h"
#include "Monitor.h"
#include <sstream>
#include <chrono>

static std::wstring EscapeJson(const std::wstring& s)
{
    std::wstring out;
    out.reserve(s.size() + 16);
    for (wchar_t ch : s)
    {
        switch (ch)
        {
        case L'\\': out += L"\\\\"; break;
        case L'\"': out += L"\\\""; break;
        case L'\n': out += L"\\n"; break;
        case L'\r': out += L"\\r"; break;
        case L'\t': out += L"\\t"; break;
        default: out += ch; break;
        }
    }
    return out;
}

bool PipeClient::NotifyService(const std::wstring& jobId,
    const std::wstring& tempFile,
    const std::wstring& documentName,
    const std::wstring& printerName)
{
    // Build JSON message with properly escaped strings
    std::wstringstream ss;
    ss << L"{"
        << L"\"Action\":\"NewJob\","
        << L"\"JobId\":" << jobId << L","
        << L"\"TempFile\":\"" << EscapeJson(tempFile) << L"\","
        << L"\"DocumentName\":\"" << EscapeJson(documentName) << L"\","
        << L"\"PrinterName\":\"" << EscapeJson(printerName) << L"\""
        << L"}";

    std::wstring json = ss.str();

    long long t0 = std::chrono::duration_cast<std::chrono::microseconds>(
        std::chrono::steady_clock::now().time_since_epoch()).count();

    DbgTrace(L"NotifyService: waiting for pipe (jobId=%s)", jobId.c_str());

    // Wait for pipe server to become available (max 2 seconds)
    // This prevents hanging the spooler if service is busy/restarting
    if (!WaitNamedPipeW(L"\\\\.\\pipe\\VirtualPrinter", 2000))
    {
        DbgTrace(L"NotifyService: WaitNamedPipe FAILED err=%lu elapsed=%lldus", GetLastError(),
            std::chrono::duration_cast<std::chrono::microseconds>(
                std::chrono::steady_clock::now().time_since_epoch()).count() - t0);
        return false;
    }

    DbgTrace(L"NotifyService: WaitNamedPipe OK, connecting...");

    HANDLE hPipe = CreateFileW(
        L"\\\\.\\pipe\\VirtualPrinter",
        GENERIC_WRITE,
        FILE_SHARE_READ | FILE_SHARE_WRITE,
        NULL,
        OPEN_EXISTING,
        0,  // synchronous I/O
        NULL);

    if (hPipe == INVALID_HANDLE_VALUE)
    {
        DbgTrace(L"NotifyService: CreateFile FAILED err=%lu", GetLastError());
        return false;
    }

    DbgTrace(L"NotifyService: connected, writing %zu bytes...", json.size() * sizeof(wchar_t));

    DWORD written;
    BOOL ok = WriteFile(hPipe, json.c_str(),
        (DWORD)(json.size() * sizeof(wchar_t)), &written, NULL);

    long long elapsed = std::chrono::duration_cast<std::chrono::microseconds>(
        std::chrono::steady_clock::now().time_since_epoch()).count() - t0;

    DbgTrace(L"NotifyService: WriteFile ok=%d written=%u elapsed=%lldus", ok, written, elapsed);

    CloseHandle(hPipe);
    return ok != FALSE;
}
