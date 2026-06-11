using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace VirtualPrinter.Service.Utils
{
    public static class SessionLauncher
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("wtsapi32.dll", SetLastError = true)]
        private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr token);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool DuplicateTokenEx(
            IntPtr existingToken,
            uint desiredAccess,
            IntPtr tokenAttributes,
            int impersonationLevel,
            int tokenType,
            out IntPtr newToken);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool SetTokenInformation(
            IntPtr token,
            TOKEN_INFORMATION_CLASS tokenInformationClass,
            ref uint tokenInformation,
            int tokenInformationLength);

        private enum TOKEN_INFORMATION_CLASS
        {
            TokenSessionId = 12
        }

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool CreateProcessAsUser(
            IntPtr token,
            string applicationName,
            string commandLine,
            IntPtr processAttributes,
            IntPtr threadAttributes,
            bool inheritHandles,
            uint creationFlags,
            IntPtr environment,
            string currentDirectory,
            ref STARTUPINFO startupInfo,
            out PROCESS_INFORMATION processInformation);

        [DllImport("userenv.dll", SetLastError = true)]
        private static extern bool CreateEnvironmentBlock(out IntPtr environment, IntPtr token, bool inherit);

        [DllImport("userenv.dll", SetLastError = true)]
        private static extern bool DestroyEnvironmentBlock(IntPtr environment);

        private const uint TOKEN_DUPLICATE = 0x0002;
        private const uint TOKEN_QUERY = 0x0008;
        private const uint TOKEN_ASSIGN_PRIMARY = 0x0001;
        private const int SecurityImpersonation = 2;
        private const int TokenPrimary = 1;
        private const uint STARTF_USESHOWWINDOW = 0x00000001;
        private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;

        [StructLayout(LayoutKind.Sequential)]
        private struct STARTUPINFO
        {
            public int cb;
            public string lpReserved;
            public string lpDesktop;
            public string lpTitle;
            public int dwX;
            public int dwY;
            public int dwXSize;
            public int dwYSize;
            public int dwXCountChars;
            public int dwYCountChars;
            public int dwFillAttribute;
            public uint dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public int dwProcessId;
            public int dwThreadId;
        }

        private static uint GetActiveConsoleSessionId()
        {
            try
            {
                var explorer = Process.GetProcessesByName("explorer")
                    .FirstOrDefault();
                if (explorer != null)
                {
                    uint sid = (uint)explorer.SessionId;
                    EventLog.WriteEntry("VirtualPrinterService",
                        $"SessionLauncher: active session={sid} (from explorer)",
                        EventLogEntryType.Information);
                    return sid;
                }
            }
            catch { }

            EventLog.WriteEntry("VirtualPrinterService",
                "SessionLauncher: explorer not found, trying console session 1",
                EventLogEntryType.Warning);
            return 1;
        }

        public static bool LaunchInActiveSession(string appPath, string args)
        {
            uint sessionId = GetActiveConsoleSessionId();

            return TryCreateProcessAsUser(sessionId, appPath, args);
        }

        private static bool TryCreateProcessAsUser(uint sessionId, string appPath, string args)
        {
            if (!WTSQueryUserToken(sessionId, out IntPtr userToken))
            {
                EventLog.WriteEntry("VirtualPrinterService",
                    $"SessionLauncher: WTSQueryUserToken failed (session={sessionId}, error={Marshal.GetLastWin32Error()})",
                    EventLogEntryType.Warning);
                return false;
            }

            try
            {
                uint desiredAccess = TOKEN_ASSIGN_PRIMARY | TOKEN_DUPLICATE | TOKEN_QUERY;
                if (!DuplicateTokenEx(userToken, desiredAccess,
                        IntPtr.Zero, SecurityImpersonation, TokenPrimary,
                        out IntPtr primaryToken))
                {
                    EventLog.WriteEntry("VirtualPrinterService",
                        $"SessionLauncher: DuplicateTokenEx failed, error={Marshal.GetLastWin32Error()}",
                        EventLogEntryType.Warning);
                    return false;
                }

                try
                {
                    if (!CreateEnvironmentBlock(out IntPtr env, primaryToken, false))
                    {
                        EventLog.WriteEntry("VirtualPrinterService",
                            $"SessionLauncher: CreateEnvironmentBlock failed, error={Marshal.GetLastWin32Error()}",
                            EventLogEntryType.Warning);
                        env = IntPtr.Zero;
                    }

                    var si = new STARTUPINFO
                    {
                        cb = Marshal.SizeOf(typeof(STARTUPINFO)),
                        lpDesktop = "winsta0\\default",
                        dwFlags = STARTF_USESHOWWINDOW,
                        wShowWindow = 1
                    };

                    string cmdLine = $"\"{appPath}\" {args}";

                    bool result = CreateProcessAsUser(
                        primaryToken,
                        null,
                        cmdLine,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        false,
                        CREATE_UNICODE_ENVIRONMENT,
                        env,
                        null,
                        ref si,
                        out var pi);

                    if (result)
                    {
                        CloseHandle(pi.hProcess);
                        CloseHandle(pi.hThread);
                    }
                    else
                    {
                        EventLog.WriteEntry("VirtualPrinterService",
                            $"SessionLauncher: CreateProcessAsUser failed for '{cmdLine}', error={Marshal.GetLastWin32Error()}",
                            EventLogEntryType.Warning);
                    }

                    if (env != IntPtr.Zero)
                        DestroyEnvironmentBlock(env);

                    return result;
                }
                finally
                {
                    CloseHandle(primaryToken);
                }
            }
            finally
            {
                CloseHandle(userToken);
            }
        }
    }
}
