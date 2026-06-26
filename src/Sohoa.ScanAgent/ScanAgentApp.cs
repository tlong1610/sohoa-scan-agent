using Sohoa.ScanAgent.Api;
using Sohoa.ScanAgent.Core.Services;
using Sohoa.ScanAgent.Services;

namespace Sohoa.ScanAgent;

/// <summary>
/// Application bootstrap: starts API on background thread, launches WinForms tray on STA thread.
/// </summary>
public static class ScanAgentApp
{
    public static System.Windows.Forms.Control? ScanDispatcher { get; private set; }
    private static CancellationTokenSource _cts = new();

    [STAThread]
    public static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var staging = new StagingService();

        // Create invisible dispatcher control on STA thread for marshalling TWAIN calls
        ScanDispatcher = new Control();
        ScanDispatcher.CreateControl();

        var trayContext = new TrayApplicationContext();
        var twain = new TwainService(staging);

        // Start API server on background thread
        _ = Task.Run(() => ApiServer.RunAsync(staging, twain, trayContext.WindowHandle, _cts.Token));

        Application.Run(trayContext);
    }

    public static void Shutdown()
    {
        _cts.Cancel();
    }
}
