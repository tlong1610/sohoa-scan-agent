using Sohoa.ScanAgent.Api;
using Sohoa.ScanAgent.Services;

namespace Sohoa.ScanAgent;

public class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly Form _mainForm;
    private readonly CancellationTokenSource _cts = new();

    public TrayApplicationContext(TwainService twain)
    {
        _mainForm = new Form
        {
            Text = "Sohoa Scan Agent",
            WindowState = FormWindowState.Minimized,
            ShowInTaskbar = false,
            Width = 1,
            Height = 1,
        };

        _mainForm.Load += (_, _) =>
        {
            _mainForm.Hide();
            ScanAgentApp.UiInvoker = _mainForm;

            twain.QueueRefreshSources(_mainForm.Handle);

            _ = Task.Run(() => ApiServer.RunAsync(
                twain,
                _mainForm.Handle,
                _cts.Token));
        };

        _mainForm.FormClosed += (_, _) => ExitThread();

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Resources", "scanner.ico");
        var icon = File.Exists(iconPath) ? new Icon(iconPath) : SystemIcons.Application;

        _trayIcon = new NotifyIcon
        {
            Icon = icon,
            Text = "Sohoa Scan Agent",
            Visible = true,
            ContextMenuStrip = BuildContextMenu(),
        };

        _trayIcon.DoubleClick += (_, _) => ShowStatusInfo();

        // Show balloon so user knows the app started (tray icon may be hidden in overflow)
        _trayIcon.BalloonTipTitle = "Sohoa Scan Agent";
        _trayIcon.BalloonTipText = "Đang chạy tại http://127.0.0.1:18612";
        _trayIcon.ShowBalloonTip(3000);

        MainForm = _mainForm;
    }

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Sohoa Scan Agent v2.0.4", null, null).Enabled = false;
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Trạng thái: Đang chạy (:18612)", null, null).Enabled = false;
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Thoát", null, OnExit);
        return menu;
    }

    private static void ShowStatusInfo()
    {
        MessageBox.Show(
            "Sohoa Scan Agent đang chạy.\nAPI: http://127.0.0.1:18612\n\nMở trình duyệt và vào app Sohoa để quét tài liệu.",
            "Sohoa Scan Agent",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void OnExit(object? sender, EventArgs e)
    {
        _trayIcon.Visible = false;
        _cts.Cancel();
        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cts.Cancel();
            _trayIcon?.Dispose();
        }
        base.Dispose(disposing);
    }
}
