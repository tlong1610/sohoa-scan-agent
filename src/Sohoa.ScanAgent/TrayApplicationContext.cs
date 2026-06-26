namespace Sohoa.ScanAgent;

/// <summary>
/// WinForms ApplicationContext that shows a system-tray icon.
/// Provides the HWND needed for TWAIN and keeps the app alive.
/// </summary>
public class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly Form _hiddenForm;

    public IntPtr WindowHandle => _hiddenForm.Handle;

    public TrayApplicationContext()
    {
        _hiddenForm = new Form
        {
            WindowState = FormWindowState.Minimized,
            ShowInTaskbar = false,
            Visible = false,
            Width = 1,
            Height = 1
        };
        _hiddenForm.Load += (_, _) => _hiddenForm.Hide();

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Resources", "scanner.ico");
        var icon = File.Exists(iconPath)
            ? new Icon(iconPath)
            : SystemIcons.Application;

        _trayIcon = new NotifyIcon
        {
            Icon = icon,
            Text = "Sohoa Scan Agent",
            Visible = true,
            ContextMenuStrip = BuildContextMenu()
        };

        _trayIcon.DoubleClick += (_, _) => ShowStatusInfo();

        Application.Run(_hiddenForm);
    }

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Sohoa Scan Agent v1.0", null, null).Enabled = false;
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
        ScanAgentApp.Shutdown();
        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _trayIcon?.Dispose();
        base.Dispose(disposing);
    }
}
