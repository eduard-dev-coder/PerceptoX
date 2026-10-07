#define UNICODE
#define _UNICODE
#include <windows.h>
#include <string>
#include <vector>

// Tiny native entrypoint. No PATH lookup, shell expansion, .NET bootstrap or external CRT.
int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR arguments, int show)
{
    std::vector<wchar_t> path(32768);
    DWORD length = GetModuleFileNameW(nullptr, path.data(), static_cast<DWORD>(path.size()));
    if (length == 0 || length >= path.size()) return 1;
    std::wstring directory(path.data(), length);
    auto separator = directory.find_last_of(L"\\/");
    if (separator == std::wstring::npos) return 1;
    directory.resize(separator);
    std::wstring runtime = directory + L"\\app";
    std::wstring executable = runtime + L"\\PerceptoX.WinUI.exe";
    if (GetFileAttributesW(executable.c_str()) == INVALID_FILE_ATTRIBUTES)
    {
        MessageBoxW(nullptr, L"PerceptoX nu poate porni. Pastreaza dosarul app langa PerceptoX.exe.\nPerceptoX cannot start. Keep the app folder next to PerceptoX.exe.", L"PerceptoX", MB_OK | MB_ICONERROR);
        return 2;
    }
    std::wstring command = L"\"" + executable + L"\"";
    if (arguments && *arguments) command += L" " + std::wstring(arguments);
    if (command.size() >= 32767) return 3;
    STARTUPINFOW startup{}; startup.cb = sizeof(startup);
    startup.dwFlags = STARTF_USESHOWWINDOW; startup.wShowWindow = static_cast<WORD>(show);
    PROCESS_INFORMATION process{};
    if (!CreateProcessW(executable.c_str(), command.data(), nullptr, nullptr, FALSE, 0, nullptr, runtime.c_str(), &startup, &process))
    {
        MessageBoxW(nullptr, L"PerceptoX nu poate porni. Reinstaleaza aplicatia sau extrage din nou pachetul complet.\nPerceptoX cannot start. Reinstall or extract the complete package again.", L"PerceptoX", MB_OK | MB_ICONERROR);
        return 4;
    }
    CloseHandle(process.hThread);
    WaitForSingleObject(process.hProcess, INFINITE);
    DWORD exitCode = 0; GetExitCodeProcess(process.hProcess, &exitCode);
    CloseHandle(process.hProcess);
    return static_cast<int>(exitCode);
}
