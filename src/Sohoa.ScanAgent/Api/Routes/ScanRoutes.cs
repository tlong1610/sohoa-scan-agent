using Sohoa.ScanAgent.Services;

namespace Sohoa.ScanAgent.Api.Routes;

public record ScanRequest(
    string? UploadUrl = null,
    bool? ShowUi = null,
    int? Dpi = null,
    string? ColorMode = null,
    string? TwainSource = null);

public static class ScanRoutes
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/scan", ScanHandler);
        app.MapPost("/documents/{documentId}/scan", ScanHandler);
    }

    private static Task<IResult> ScanHandler(
        ScanRequest req,
        TwainService twain,
        WindowHandleAccessor hwnd,
        string? documentId = null) =>
        ExecuteScan(req, twain, hwnd);

    private static async Task<IResult> ExecuteScan(
        ScanRequest req,
        TwainService twain,
        WindowHandleAccessor hwnd)
    {
        try
        {
            var invoker = ScanAgentApp.UiInvoker;
            if (invoker is null || invoker.IsDisposed)
                return Results.Problem("Scan Agent UI is not ready. Restart SohoaScanAgent.exe.");

            var tcs = new TaskCompletionSource<(byte[]? Jpeg, bool Uploaded, bool Cancelled)>();

            invoker.BeginInvoke(() =>
            {
                try
                {
                    var jpeg = twain.ScanOnePageJpeg(
                        req.ShowUi ?? true,
                        req.Dpi ?? 300,
                        req.ColorMode ?? "bw",
                        req.TwainSource,
                        hwnd.Handle);

                    if (jpeg is null)
                    {
                        tcs.TrySetResult((null, false, true));
                        return;
                    }

                    if (!string.IsNullOrWhiteSpace(req.UploadUrl))
                    {
                        twain.UploadJpegAsync(req.UploadUrl, jpeg)
                            .ContinueWith(t =>
                            {
                                if (t.IsFaulted)
                                    tcs.TrySetException(t.Exception!.GetBaseException());
                                else
                                    tcs.TrySetResult((null, true, false));
                            });
                        return;
                    }

                    tcs.TrySetResult((jpeg, false, false));
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });

            var result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(130));

            if (result.Uploaded)
                return Results.Ok(new { ok = true, uploaded = true });

            if (result.Cancelled)
                return Results.Ok(new { ok = true, cancelled = true });

            return Results.File(result.Jpeg!, "image/jpeg");
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message);
        }
    }
}
