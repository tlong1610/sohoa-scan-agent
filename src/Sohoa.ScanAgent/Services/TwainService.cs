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
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromMinutes(2),
    };

    public List<string> GetSources()
    {
        var session = new TwainSession(BuildAppId());
        session.Open();
        var names = session.Select(s => s.Name).ToList();
        session.Close();
        return names;
    }

    /// <summary>
    /// Scan one or more pages on STA thread.
    /// <paramref name="adf"/> scans until the feeder is empty.
    /// <paramref name="duplex"/> enables double-sided scanning (implies ADF).
    /// Returns null if the user cancelled.
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
        var batchMode = adf || duplex;
        var pages = new List<byte[]>();

        var twainSession = new TwainSession(BuildAppId());
        twainSession.Open(new WindowsFormsMessageLoopHook(windowHandle));

        var source = ResolveSource(twainSession, twainSource);
        source.Open();
        ConfigureSource(source, dpi, colorMode, adf, duplex);

        var tcs = new TaskCompletionSource<List<byte[]>?>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        twainSession.TransferReady += (_, e) =>
        {
            if (!batchMode && pages.Count >= 1)
                e.CancelAll = true;
            else
                e.CancelAll = false;
        };

        twainSession.DataTransferred += (_, e) =>
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
        };

        twainSession.TransferError += (_, e) =>
        {
            tcs.TrySetException(
                e.Exception ?? new InvalidOperationException("Unknown TWAIN transfer error"));
        };

        twainSession.SourceDisabled += (_, _) =>
        {
            if (batchMode)
                tcs.TrySetResult(pages.Count > 0 ? pages : null);
            else if (!tcs.Task.IsCompleted)
                tcs.TrySetResult(null);
        };

        source.Enable(
            showUi ? SourceEnableMode.ShowUI : SourceEnableMode.NoUI,
            showUi,
            windowHandle);

        var timeoutSeconds = batchMode ? 600 : 120;
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (!tcs.Task.IsCompleted)
        {
            if (DateTime.UtcNow >= deadline)
            {
                tcs.TrySetResult(batchMode && pages.Count > 0 ? pages : null);
                break;
            }

            Application.DoEvents();
            Thread.Sleep(10);
        }

        source.Close();
        twainSession.Close();

        if (tcs.Task.IsFaulted)
        {
            throw tcs.Task.Exception?.GetBaseException()
                ?? new InvalidOperationException("TWAIN scan failed");
        }

        return tcs.Task.IsCompletedSuccessfully ? tcs.Task.Result : null;
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

    /// <summary>
    /// TWAIN native transfers are often 1bpp indexed BMP/DIB — GDI+ JPEG encoder fails on those.
    /// SkiaSharp handles all common TWAIN pixel formats.
    /// </summary>
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

        source.Capabilities.ICapXResolution.SetValue((TWFix32)dpi);
        source.Capabilities.ICapYResolution.SetValue((TWFix32)dpi);
        source.Capabilities.ICapPixelType.SetValue(pixelType);
        source.Capabilities.ICapXferMech.SetValue(XferMech.Native);

        var useFeeder = adf || duplex;

        if (source.Capabilities.CapFeederEnabled.CanSet)
            source.Capabilities.CapFeederEnabled.SetValue(useFeeder ? BoolType.True : BoolType.False);

        if (source.Capabilities.CapAutoFeed.CanSet)
            source.Capabilities.CapAutoFeed.SetValue(useFeeder ? BoolType.True : BoolType.False);

        if (source.Capabilities.CapDuplexEnabled.CanSet)
        {
            source.Capabilities.CapDuplexEnabled.SetValue(duplex ? BoolType.True : BoolType.False);
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
