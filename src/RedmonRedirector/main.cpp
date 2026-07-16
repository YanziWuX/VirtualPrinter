#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <winspool.h>
#include <string>
#include <vector>
#include <sstream>
#include <fstream>
#include <io.h>
#include <fcntl.h>

static std::wstring EscapeJson(const std::wstring& s) {
    std::wstring out;
    out.reserve(s.size() + 16);
    for (wchar_t ch : s) {
        switch (ch) {
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

static bool ReadStdin(std::vector<char>& out) {
    out.clear();
    char buf[65536];
    for (;;) {
        DWORD read = 0;
        if (!ReadFile(GetStdHandle(STD_INPUT_HANDLE), buf, sizeof(buf), &read, NULL))
            break;
        if (read == 0)
            break;
        out.insert(out.end(), buf, buf + read);
    }
    return !out.empty();
}

static std::wstring SaveTempFile(const std::vector<char>& data) {
    wchar_t tempDir[MAX_PATH];
    if (!GetTempPathW(MAX_PATH, tempDir))
        return L"";
    wchar_t tempFile[MAX_PATH];
    if (!GetTempFileNameW(tempDir, L"VPP", 0, tempFile))
        return L"";
    // Rename to .ps extension
    std::wstring psFile = tempFile;
    psFile += L".ps";
    MoveFileExW(tempFile, psFile.c_str(), MOVEFILE_REPLACE_EXISTING);

    std::ofstream ofs(psFile, std::ios::binary);
    if (!ofs.is_open())
        return L"";
    ofs.write(data.data(), data.size());
    ofs.close();
    return psFile;
}

static bool NotifyService(const std::wstring& jobId, const std::wstring& tempFile,
    const std::wstring& documentName, const std::wstring& printerName)
{
    std::wstringstream ss;
    ss << L"{\"Action\":\"NewJob\","
        << L"\"JobId\":" << jobId << L","
        << L"\"TempFile\":\"" << EscapeJson(tempFile) << L"\","
        << L"\"DocumentName\":\"" << EscapeJson(documentName) << L"\","
        << L"\"PrinterName\":\"" << EscapeJson(printerName) << L"\"}";
    std::wstring json = ss.str();

    if (!WaitNamedPipeW(L"\\\\.\\pipe\\VirtualPrinter", 5000))
        return false;

    HANDLE hPipe = CreateFileW(L"\\\\.\\pipe\\VirtualPrinter",
        GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
        NULL, OPEN_EXISTING, 0, NULL);
    if (hPipe == INVALID_HANDLE_VALUE)
        return false;

    DWORD written;
    BOOL ok = WriteFile(hPipe, json.c_str(),
        (DWORD)(json.size() * sizeof(wchar_t)), &written, NULL);
    CloseHandle(hPipe);
    return ok != FALSE;
}

int wmain(int argc, wchar_t* argv[]) {
    // Set stdin to binary mode
    _setmode(_fileno(stdin), _O_BINARY);

    // Parse args: [port, jobId, printer, document]
    std::wstring jobId = L"0";
    std::wstring printerName = L"VirtualPrinter";
    std::wstring documentName = L"Print Job";
    std::wstring portName = L"";

    if (argc > 1) portName = argv[1];
    if (argc > 2) jobId = argv[2];
    if (argc > 3) printerName = argv[3];
    if (argc > 4) documentName = argv[4];

    // Read stdin
    std::vector<char> data;
    if (!ReadStdin(data))
        return 1;

    // Try to get real document name from job info if not provided
    if (documentName == L"Print Job" || documentName.empty()) {
        wchar_t docName[256] = { 0 };
        DWORD size = 256;
        if (GetJobW(NULL, _wtoi(jobId.c_str()), 1, (LPBYTE)docName, size, &size))
            documentName = docName;
    }

    // Save to temp file
    std::wstring tempFile = SaveTempFile(data);
    if (tempFile.empty())
        return 1;

    // Notify service
    if (!NotifyService(jobId, tempFile, documentName, printerName))
        return 1;

    return 0;
}
