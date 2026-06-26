using System.Reflection;
using NTwain;
using NTwain.Data;
using Sohoa.ScanAgent.Core.Models;
using Sohoa.ScanAgent.Core.Services;

namespace Sohoa.ScanAgent.Services;

/// <summary>
/// Wraps NTwain 3.x to scan one page at a time.
/// Events (TransferReady, DataTransferred, TransferError, SourceDisabled) live on TwainSession.
/// Must be invoked on the STA (WinForms) thread because TWAIN requires a window handle.
/// </summary>
public class TwainService
{
    private readonly StagingService _staging;

    public TwainService(StagingService staging)
    {
        _staging = staging;
    }

    /// <summary>Returns names of all available TWAIN data sources on this machine.</summary>
    public List<string> GetSources()
    {
        var session = new TwainSession(BuildAppId());
        session.Open();
        var names = session.Select(s => s.Name).ToList();
        session.Close();
        return names;
    }

    /// <summary>
    /// Scans a single page and appends it to the given document.
    /// showUi = true opens the scanner's TWAIN UI dialog.
    /// Must be called on the STA thread; blocks until transfer or cancel.
    /// Returns the new PageMeta on success, null if the user cancelled.
    /// </summary>
    public PageMeta? ScanOnePage(
        string sessionId,
        string dossierId,
        string documentId,
        bool showUi,
        int dpi,
        string colorMode,
        IntPtr windowHandle)
    {
        var twainSession = new TwainSession(BuildAppId());
        twainSession.Open(new WindowsFormsMessageLoopHook(windowHandle));

        var source = twainSession.FirstOrDefault()
            ?? throw new InvalidOperationException(
                "No TWAIN source found. Please install the Plustek PS4080U driver.");

        source.Open();
        ConfigureSource(source, dpi, colorMode);

        var tcs = new TaskCompletionSource<PageMeta?>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        twainSession.TransferReady += (s, e) =>
        {
            e.CancelAll = false;
        };

        twainSession.DataTransferred += (s, e) =>
        {
            try
            {
                var imageStream = e.GetNativeImageStream();
                if (imageStream != null)
                {
                    var pageId = Guid.NewGuid().ToString();
                    var tiffPath = _staging.GetPageTiffPath(sessionId, dossierId, documentId, pageId);
                    Directory.CreateDirectory(Path.GetDirectoryName(tiffPath)!);

                    using var bmp = System.Drawing.Image.FromStream(imageStream);
                    bmp.Save(tiffPath, System.Drawing.Imaging.ImageFormat.Tiff);
                    imageStream.Dispose();

                    var page = _staging.AddPage(sessionId, dossierId, documentId, tiffPath);
                    tcs.TrySetResult(page);
                }
                else
                {
                    tcs.TrySetResult(null);
                }
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        };

        twainSession.TransferError += (s, e) =>
        {
            tcs.TrySetException(
                e.Exception ?? new InvalidOperationException("Unknown TWAIN transfer error"));
        };

        twainSession.SourceDisabled += (s, e) =>
        {
            // User closed scan UI without scanning
            tcs.TrySetResult(null);
        };

        source.Enable(
            showUi ? SourceEnableMode.ShowUI : SourceEnableMode.NoUI,
            showUi,
            windowHandle);

        var completed = tcs.Task.Wait(TimeSpan.FromSeconds(120));

        source.Close();
        twainSession.Close();

        if (!completed) return null;
        return tcs.Task.IsCompletedSuccessfully ? tcs.Task.Result : null;
    }

    private static void ConfigureSource(DataSource source, int dpi, string colorMode)
    {
        var pixelType = colorMode?.ToLowerInvariant() switch
        {
            "color" => PixelType.RGB,
            "gray" or "grayscale" => PixelType.Gray,
            _ => PixelType.BlackWhite
        };

        source.Capabilities.ICapXResolution.SetValue((TWFix32)dpi);
        source.Capabilities.ICapYResolution.SetValue((TWFix32)dpi);
        source.Capabilities.ICapPixelType.SetValue(pixelType);
        source.Capabilities.ICapXferMech.SetValue(XferMech.Native);

        if (source.Capabilities.CapFeederEnabled.CanSet)
            source.Capabilities.CapFeederEnabled.SetValue(BoolType.True);
    }

    private static TWIdentity BuildAppId()
        => TWIdentity.CreateFromAssembly(DataGroups.Image, Assembly.GetExecutingAssembly());
}
