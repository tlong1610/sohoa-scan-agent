using System.Net.Http.Headers;
using System.Reflection;
using NTwain;
using NTwain.Data;
using SkiaSharp;

namespace Sohoa.ScanAgent.Services;

/// <summary>
/// Stateless TWAIN bridge — scans to JPEG in memory, optional upload to presigned URL.
/// TWAIN methods must run on the WinForms STA thread.
/// </summary>
public class TwainService
{
    private static readonly object ScanSync = new();
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromMinutes(2),
    };

    public List<string> GetSources()
    {
        var session = new TwainSession(BuildAppId());
        try
        {
            session.Open();
            return session.Select(s => s.Name).ToList();
        }
        finally
        {
            SafeClose(session, source: null);
        }
    }

    /// <summary>
    /// Scan one or more pages on STA thread.
    /// <paramref name="adf"/> scans until the feeder is empty.
    /// <paramref name="duplex"/> enables double-sided scanning (implies ADF).
    /// Returns null if the user cancelled or the feeder is empty.
    /// </summary>
    public List<byte[]>? ScanPagesJpeg(
        bool showUi,
        int dpi,
        string colorMode,
        string? twainSource,
        bool adf,
        bool duplex,
        IntPtr windowHandle)
    {
        lock (ScanSync)
        {
            var batchMode = adf || duplex;
            var pages = new List<byte[]>();
            TwainSession? twainSession = null;
            DataSource? source = null;

            var tcs = new TaskCompletionSource<List<byte[]>?>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            try
            {
                twainSession = new TwainSession(BuildAppId());
                twainSession.StopOnTransferError = false;
                twainSession.Open(new WindowsFormsMessageLoopHook(windowHandle));

                source = ResolveSource(twainSession, twainSource);
                source.Open();
                ConfigureSource(source, dpi, colorMode, adf, duplex);

                twainSession.TransferReady += OnTransferReady;
                twainSession.DataTransferred += OnDataTransferred;
                twainSession.TransferError += OnTransferError;
                twainSession.SourceDisabled += OnSourceDisabled;

                var enableRc = source.Enable(
                    showUi ? SourceEnableMode.ShowUI : SourceEnableMode.NoUI,
                    showUi,
                    windowHandle);

                if (enableRc != ReturnCode.Success)
                {
                    tcs.TrySetResult(null);
                }
                else
                {
                    var timeoutSeconds = batchMode ? 600 : 120;
                    var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
                    while (!tcs.Task.IsCompleted)
                    {
                        if (DateTime.UtcNow >= deadline)
                        {
                            tcs.TrySetResult(pages.Count > 0 ? pages : null);
                            break;
                        }

                        Application.DoEvents();
                        Thread.Sleep(10);
                    }
                }

                if (tcs.Task.IsFaulted)
                {
                    throw tcs.Task.Exception?.GetBaseException()
                        ?? new InvalidOperationException("TWAIN scan failed");
                }

                return tcs.Task.IsCompletedSuccessfully ? tcs.Task.Result : null;
            }
            finally
            {
                if (twainSession is not null)
                {
                    twainSession.TransferReady -= OnTransferReady;
                    twainSession.DataTransferred -= OnDataTransferred;
                    twainSession.TransferError -= OnTransferError;
                    twainSession.SourceDisabled -= OnSourceDisabled;
                }

                SafeClose(twainSession, source);
            }

            void OnTransferReady(object? _, TransferReadyEventArgs e)
            {
                if (!batchMode && pages.Count >= 1)
                    e.CancelAll = true;
                else
                    e.CancelAll = false;
            }

            void OnDataTransferred(object? _, DataTransferredEventArgs e)
            {
                try
                {
                    var imageStream = e.GetNativeImageStream();
                    if (imageStream is null)
                        return;

                    using (imageStream)
                    using (var buffer = new MemoryStream())
                    {
                        imageStream.CopyTo(buffer);
                        buffer.Position = 0;
                        pages.Add(EncodeJpeg(buffer));
                    }

                    if (!batchMode)
                        tcs.TrySetResult(pages);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }

            void OnTransferError(object? _, TransferErrorEventArgs e)
            {
                // End of ADF / no paper is normal after the last sheet — keep pages scanned so far.
                if (pages.Count > 0)
                {
                    tcs.TrySetResult(pages);
                    return;
                }

                tcs.TrySetResult(null);
            }

            void OnSourceDisabled(object? sender, EventArgs args)
            {
                if (batchMode)
                    tcs.TrySetResult(pages.Count > 0 ? pages : null);
                else if (!tcs.Task.IsCompleted)
                    tcs.TrySetResult(pages.Count > 0 ? pages : null);
            }
        }
    }

    public async Task UploadJpegAsync(string uploadUrl, byte[] jpegBytes)
    {
        using var content = new ByteArrayContent(jpegBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");

        using var response = await Http.PutAsync(uploadUrl, content);
        response.EnsureSuccessStatusCode();
    }

    public async Task UploadJpegBatchAsync(IReadOnlyList<string> uploadUrls, IReadOnlyList<byte[]> pages)
    {
        if (pages.Count > uploadUrls.Count)
        {
            throw new InvalidOperationException(
                $"Scanner returned {pages.Count} page(s) but only {uploadUrls.Count} upload URL(s) were provided.");
        }

        for (var i = 0; i < pages.Count; i++)
            await UploadJpegAsync(uploadUrls[i], pages[i]);
    }

    private static DataSource ResolveSource(TwainSession session, string? twainSource)
    {
        if (!string.IsNullOrWhiteSpace(twainSource))
        {
            var named = session.FirstOrDefault(s =>
                s.Name.Equals(twainSource, StringComparison.OrdinalIgnoreCase));
            if (named is not null) return named;
        }

        return session.FirstOrDefault(s =>
                s.Name.Contains("plustek", StringComparison.OrdinalIgnoreCase) &&
                s.Name.Contains("twain", StringComparison.OrdinalIgnoreCase))
            ?? session.FirstOrDefault(s =>
                s.Name.Contains("plustek", StringComparison.OrdinalIgnoreCase) ||
                s.Name.Contains("ps4080", StringComparison.OrdinalIgnoreCase))
            ?? session.FirstOrDefault()
            ?? throw new InvalidOperationException(
                "No TWAIN source found. Install the Plustek PS4080U TWAIN driver and use the 32-bit (x86) Scan Agent build.");
    }

    private static byte[] EncodeJpeg(Stream imageStream)
    {
        using var bitmap = SKBitmap.Decode(imageStream)
            ?? throw new InvalidOperationException("TWAIN returned an unreadable image.");

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 88)
            ?? throw new InvalidOperationException("Failed to encode scan as JPEG.");

        return data.ToArray();
    }

    private static void ConfigureSource(DataSource source, int dpi, string colorMode, bool adf, bool duplex)
    {
        var pixelType = colorMode?.ToLowerInvariant() switch
        {
            "color" => PixelType.RGB,
            "gray" or "grayscale" => PixelType.Gray,
            _ => PixelType.BlackWhite,
        };

        SetCap(source.Capabilities.ICapXResolution, (TWFix32)dpi);
        SetCap(source.Capabilities.ICapYResolution, (TWFix32)dpi);
        SetCap(source.Capabilities.ICapPixelType, pixelType);
        SetCap(source.Capabilities.ICapXferMech, XferMech.Native);

        var useFeeder = adf || duplex;

        SetCap(source.Capabilities.CapFeederEnabled, useFeeder ? BoolType.True : BoolType.False);
        SetCap(source.Capabilities.CapAutoFeed, useFeeder ? BoolType.True : BoolType.False);
        SetCap(source.Capabilities.CapDuplexEnabled, duplex ? BoolType.True : BoolType.False);
    }

    private static void SetCap<T>(ICapWrapper<T> cap, T value)
    {
        try
        {
            if (cap.CanSet)
                cap.SetValue(value);
        }
        catch
        {
            // Driver may not support this capability — ignore.
        }
    }

    private static void SafeClose(TwainSession? session, DataSource? source)
    {
        if (session is null)
            return;

        try
        {
            if (source is not null && session.State >= 4)
                source.Close();
        }
        catch
        {
            // TWAIN driver may already be disabled after feeder empty.
        }

        try
        {
            if (session.State >= 3)
                session.Close();
        }
        catch
        {
            // Best-effort cleanup so the next scan can open a fresh session.
        }
    }

    private static TWIdentity BuildAppId()
    {
        var assembly = Assembly.GetExecutingAssembly();
        if (!string.IsNullOrEmpty(assembly.Location))
            return TWIdentity.CreateFromAssembly(DataGroups.Image, assembly);

        return TWIdentity.Create(
            DataGroups.Image,
            assembly.GetName().Version ?? new Version(2, 0, 0),
            "Sohoa",
            "Scan Agent",
            "Sohoa Scan Agent",
            ResolveExecutablePath());
    }

    private static string ResolveExecutablePath()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath))
        {
            var args = Environment.GetCommandLineArgs();
            exePath = args.Length > 0 ? args[0] : null;
        }

        if (string.IsNullOrWhiteSpace(exePath))
            exePath = Path.Combine(AppContext.BaseDirectory, "SohoaScanAgent.exe");

        return Path.GetFullPath(exePath);
    }
}
