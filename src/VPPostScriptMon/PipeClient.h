#pragma once
#include <windows.h>
#include <string>

class PipeClient {
public:
    static bool NotifyService(const std::wstring& jobId,
        const std::wstring& tempFile,
        const std::wstring& documentName,
        const std::wstring& printerName);
};
