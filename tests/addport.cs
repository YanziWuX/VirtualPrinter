using System;
using System.IO;
using System.Runtime.InteropServices;

class AddPort
{
    [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

    [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool XcvData(IntPtr hXcv, string pszDataName, IntPtr pInputData, int cbInputData, IntPtr pOutputData, int cbOutputData, out int pcbOutputNeeded, out int pdwStatus);

    static void Main()
    {
        string log = @"D:\Code\YanziWu VirtualPrinter\tests\_xcv.log";
        File.WriteAllText(log, "Starting...\r\n");

        // Open Xcv handle for the monitor
        IntPtr hPrinter;
        bool ok = OpenPrinter(",XcvMonitor Multi File Port Monitor", out hPrinter, IntPtr.Zero);
        int err = Marshal.GetLastWin32Error();
        File.AppendAllText(log, string.Format("OpenPrinter: ok={0}, err={1}, handle={2}\r\n", ok, err, hPrinter));

        if (!ok || hPrinter == IntPtr.Zero)
        {
            // Try alternative name
            ok = OpenPrinter(",XcvMonitor mfilemon", out hPrinter, IntPtr.Zero);
            err = Marshal.GetLastWin32Error();
            File.AppendAllText(log, string.Format("OpenPrinter(alt): ok={0}, err={1}, handle={2}\r\n", ok, err, hPrinter));
        }

        if (ok && hPrinter != IntPtr.Zero)
        {
            // Send AddPort command
            string portName = "GSPDF:\0";
            IntPtr pInput = Marshal.StringToCoTaskMemUni(portName);
            int pcbNeeded, dwStatus;
            bool xcvOk = XcvData(hPrinter, "AddPort", pInput, portName.Length * 2, IntPtr.Zero, 0, out pcbNeeded, out dwStatus);
            int xcvErr = Marshal.GetLastWin32Error();
            File.AppendAllText(log, string.Format("XcvData AddPort: ok={0}, err={1}, status={2}\r\n", xcvOk, xcvErr, dwStatus));

            // Also try port name without colon
            portName = "GSPDF\0";
            pInput = Marshal.StringToCoTaskMemUni(portName);
            xcvOk = XcvData(hPrinter, "AddPort", pInput, portName.Length * 2, IntPtr.Zero, 0, out pcbNeeded, out dwStatus);
            xcvErr = Marshal.GetLastWin32Error();
            File.AppendAllText(log, string.Format("XcvData AddPort (no colon): ok={0}, err={1}, status={2}\r\n", xcvOk, xcvErr, dwStatus));

            Marshal.FreeCoTaskMem(pInput);
            ClosePrinter(hPrinter);
        }

        File.AppendAllText(log, "Done.\r\n");
    }
}
