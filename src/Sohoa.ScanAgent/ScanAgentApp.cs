using Sohoa.ScanAgent.Core.Services;
using Sohoa.ScanAgent.Services;

namespace Sohoa.ScanAgent;

public static class ScanAgentApp
{
    /// <summary>WinForms control on the STA thread (hidden main form) for TWAIN BeginInvoke.</summary>
    public static Control? UiInvoker { get; set; }

    [STAThread]
    public static void Main()
    {
        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) => ShowFatalError(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                ShowFatalError(e.ExceptionObject as Exception);

            var staging = new StagingService();
            var twain = new TwainService(staging);

            Application.Run(new TrayApplicationContext(staging, twain));
        }
        catch (Exception ex)
        {
            ShowFatalError(ex);
        }
    }

    private static void ShowFatalError(Exception? ex)
    {
        var message = ex?.Message ?? "Unknown error";
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SohoaScanAgent", "error.log");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.AppendAllText(logPath, $"[{DateTime.Now:O}] {ex}\n\n");
        }
        catch { /* ignore log failures */ }

        MessageBox.Show(
            $"Sohoa Scan Agent không khởi động được.\n\n{message}\n\nChi tiết: {logPath}",
            "Sohoa Scan Agent — Lỗi",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}
