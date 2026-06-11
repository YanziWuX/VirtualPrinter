#include "Port.h"
#include "PipeClient.h"
#include "Monitor.h"
#include <shlobj.h>
#include <sstream>
#include <thread>
#include <chrono>

static long long GetElapsedUs(long long t0)
{
    auto now = std::chrono::steady_clock::now();
    return std::chrono::duration_cast<std::chrono::microseconds>(now.time_since_epoch()).count() - t0;
}

PortContext* PortContext::Create(const std::wstring& portName)
{
    return new PortContext(portName);
}

PortContext::PortContext(const std::wstring& portName)
    : m_portName(portName)
    , m_jobId(0)
    , m_hFile(INVALID_HANDLE_VALUE)
    , m_inJob(false)
{
}

PortContext::~PortContext()
{
    if (m_hFile != INVALID_HANDLE_VALUE)
        CloseHandle(m_hFile);
}

bool PortContext::EnsureTempDir()
{
    WCHAR tempPath[MAX_PATH];
    if (!GetTempPathW(MAX_PATH, tempPath))
        return false;

    std::wstring dir = std::wstring(tempPath) + L"VirtualPrinter\\";
    if (!CreateDirectoryW(dir.c_str(), NULL))
    {
        if (GetLastError() != ERROR_ALREADY_EXISTS)
            return false;
    }
    return true;
}

BOOL PortContext::StartDoc(LPWSTR pszPrinterName, DWORD JobId, PDOC_INFO_1W pDocInfo)
{
    long long t0 = std::chrono::duration_cast<std::chrono::microseconds>(
        std::chrono::steady_clock::now().time_since_epoch()).count();

    if (m_inJob)
    {
        DbgTrace(L"StartDoc: force-ending stale job %d", m_jobId);
        EndDoc();
    }

    m_jobId = JobId;
    m_printerName = pszPrinterName ? pszPrinterName : L"";
    m_documentName = pDocInfo && pDocInfo->pDocName ? pDocInfo->pDocName : L"Untitled";

    DbgTrace(L"StartDoc: ensure temp dir...");
    if (!EnsureTempDir())
    {
        DbgTrace(L"StartDoc: EnsureTempDir FAILED err=%lu elapsed=%lldus", GetLastError(), GetElapsedUs(t0));
        return FALSE;
    }

    WCHAR tempPath[MAX_PATH];
    if (!GetTempPathW(MAX_PATH, tempPath))
    {
        DbgTrace(L"StartDoc: GetTempPathW FAILED err=%lu elapsed=%lldus", GetLastError(), GetElapsedUs(t0));
        return FALSE;
    }
    DbgTrace(L"StartDoc: tempPath=%s elapsed=%lldus", tempPath, GetElapsedUs(t0));

    wchar_t buf[64];
    swprintf_s(buf, L"%d.tmp", JobId);
    m_tempFile = std::wstring(tempPath) + L"VirtualPrinter\\" + buf;
    DbgTrace(L"StartDoc: creating file=%s", m_tempFile.c_str());

    m_hFile = CreateFileW(m_tempFile.c_str(), GENERIC_WRITE,
        FILE_SHARE_READ, NULL, CREATE_ALWAYS,
        FILE_ATTRIBUTE_TEMPORARY, NULL);

    if (m_hFile == INVALID_HANDLE_VALUE)
    {
        DbgTrace(L"StartDoc: CreateFileW FAILED err=%lu elapsed=%lldus", GetLastError(), GetElapsedUs(t0));
        return FALSE;
    }

    DbgTrace(L"StartDoc: file created OK");
    m_inJob = true;
    DbgTrace(L"StartDoc: returning TRUE jobId=%d elapsed=%lldus", JobId, GetElapsedUs(t0));
    return TRUE;
}

BOOL PortContext::Write(LPBYTE pBuffer, DWORD cbBuf, LPDWORD pcbWritten)
{
    if (!m_inJob || m_hFile == INVALID_HANDLE_VALUE)
        return FALSE;

    return WriteFile(m_hFile, pBuffer, cbBuf, pcbWritten, NULL);
}

BOOL PortContext::EndDoc(void)
{
    long long t0 = std::chrono::duration_cast<std::chrono::microseconds>(
        std::chrono::steady_clock::now().time_since_epoch()).count();

    if (!m_inJob)
    {
        DbgTrace(L"EndDoc: not in a job elapsed=%lldus", GetElapsedUs(t0));
        return FALSE;
    }

    m_inJob = false;

    if (m_hFile != INVALID_HANDLE_VALUE)
    {
        CloseHandle(m_hFile);
        m_hFile = INVALID_HANDLE_VALUE;
    }

    DbgTrace(L"EndDoc: file closed, calling NotifyService elapsed=%lldus", GetElapsedUs(t0));
    NotifyService();
    DbgTrace(L"EndDoc: returning TRUE jobId=%d elapsed=%lldus", m_jobId, GetElapsedUs(t0));
    return TRUE;
}

BOOL PortContext::Close(void)
{
    if (m_inJob)
        EndDoc();

    if (m_hFile != INVALID_HANDLE_VALUE)
    {
        CloseHandle(m_hFile);
        m_hFile = INVALID_HANDLE_VALUE;
    }
    return TRUE;
}

bool PortContext::NotifyService()
{
    long long t0 = std::chrono::duration_cast<std::chrono::microseconds>(
        std::chrono::steady_clock::now().time_since_epoch()).count();

    // Copy data before launching thread (PortContext may be destroyed after EndDoc)
    std::wstring jobId = std::to_wstring(m_jobId);
    std::wstring tempFile = m_tempFile;
    std::wstring documentName = m_documentName;
    std::wstring printerName = m_printerName;

    DbgTrace(L"NotifyService: launching notification thread for job %s", jobId.c_str());

    // Run notification on a separate thread so the spooler is never blocked
    std::thread t([jobId, tempFile, documentName, printerName]() {
        DbgTrace(L"NotifyService: thread started, calling PipeClient::NotifyService...");
        bool ok = PipeClient::NotifyService(jobId, tempFile, documentName, printerName);
        DbgTrace(L"NotifyService: thread done ok=%d", ok);
    });
    t.detach();

    DbgTrace(L"NotifyService: thread launched elapsed=%lldus", GetElapsedUs(t0));
    return true;
}
