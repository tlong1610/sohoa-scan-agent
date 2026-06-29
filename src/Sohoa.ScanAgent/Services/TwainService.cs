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

    /// <summary>Scan one page on STA thread. Returns null if cancelled.</summary>
    public byte[]? ScanOnePageJpeg(
        bool showUi,
        int dpi,
        string colorMode,
        string? twainSource,
        IntPtr windowHandle)
    {
        var twainSession = new TwainSession(BuildAppId());
        twainSession.Open(new WindowsFormsMessageLoopHook(windowHandle));

        var source = ResolveSource(twainSession, twainSource);
        source.Open();
        ConfigureSource(source, dpi, colorMode);

        var tcs = new TaskCompletionSource<byte[]?>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        twainSession.TransferReady += (_, e) => e.CancelAll = false;

        twainSession.DataTransferred += (_, e) =>
        {
            try
            {
                var imageStream = e.GetNativeImageStream();
                if (imageStream is null)
                {
                    tcs.TrySetResult(null);
                    return;
                }

                using (imageStream)
                using (var buffer = new MemoryStream())
                {
                    imageStream.CopyTo(buffer);
                    buffer.Position = 0;
                    tcs.TrySetResult(EncodeJpeg(buffer));
                }
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

        twainSession.SourceDisabled += (_, _) => tcs.TrySetResult(null);

        source.Enable(
            showUi ? SourceEnableMode.ShowUI : SourceEnableMode.NoUI,
            showUi,
            windowHandle);

        var deadline = DateTime.UtcNow.AddSeconds(120);
        while (!tcs.Task.IsCompleted)
        {
            if (DateTime.UtcNow >= deadline)
            {
                tcs.TrySetResult(null);
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

    private static void ConfigureSource(DataSource source, int dpi, string colorMode)
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

        if (source.Capabilities.CapFeederEnabled.CanSet)
            source.Capabilities.CapFeederEnabled.SetValue(BoolType.True);
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
