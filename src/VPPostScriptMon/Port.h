#pragma once
#include <windows.h>
#include <winspool.h>
#include <string>

void DbgTrace(const wchar_t* fmt, ...);

class PortContext {
public:
    static PortContext* Create(const std::wstring& portName);
    ~PortContext();

    BOOL StartDoc(LPWSTR pszPrinterName, DWORD JobId, PDOC_INFO_1W pDocInfo);
    BOOL Write(LPBYTE pBuffer, DWORD cbBuf, LPDWORD pcbWritten);
    BOOL EndDoc(void);
    BOOL Close(void);

    const std::wstring& PortName() const { return m_portName; }

private:
    PortContext(const std::wstring& portName);
    bool EnsureTempDir();
    bool NotifyService();

    std::wstring m_portName;
    std::wstring m_tempFile;
    std::wstring m_documentName;
    std::wstring m_printerName;
    DWORD m_jobId;
    HANDLE m_hFile;
    bool m_inJob;
};
