using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace MikuDancePackager;

internal static class Program
{
    private const string SingleInstanceMutexName = @"Local\MikuDancePackager.SingleInstance";
    private const int SwRestore = 9;
    private static readonly string StartupErrorLogPath =
        Path.Combine(Path.GetTempPath(), "MikuDancePackager-startup-error.log");

    [STAThread]
    private static void Main()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, args) => ShowStartupError(args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            ShowStartupError(args.ExceptionObject as Exception ?? new Exception("Unknown startup failure."));

        try
        {
            using var mutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
            if (!createdNew)
            {
                TryActivateExistingInstance();
                MessageBox.Show(
                    "\u5DF2\u68C0\u6D4B\u5230 Miku Dance Packager \u6B63\u5728\u8FD0\u884C\uFF0C\u5DF2\u5C1D\u8BD5\u5207\u6362\u5230\u73B0\u6709\u7A97\u53E3\u3002",
                    "Miku Dance Packager",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
        catch (Exception exception)
        {
            ShowStartupError(exception);
        }
    }

    private static void TryActivateExistingInstance()
    {
        var currentProcess = Process.GetCurrentProcess();
        for (var attempt = 0; attempt < 10; attempt++)
        {
            foreach (var process in Process.GetProcessesByName(currentProcess.ProcessName))
            {
                if (process.Id == currentProcess.Id)
                {
                    continue;
                }

                var mainWindowHandle = process.MainWindowHandle;
                if (mainWindowHandle == IntPtr.Zero)
                {
                    continue;
                }

                if (IsIconic(mainWindowHandle))
                {
                    ShowWindowAsync(mainWindowHandle, SwRestore);
                }

                SetForegroundWindow(mainWindowHandle);
                return;
            }

            Thread.Sleep(150);
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    private static void ShowStartupError(Exception exception)
    {
        try
        {
            File.WriteAllText(StartupErrorLogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}{exception}");
            MessageBox.Show(
                $"{exception}{Environment.NewLine}{Environment.NewLine}Startup log: {StartupErrorLogPath}",
                "Miku Dance Packager Startup Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch
        {
        }
    }
}
