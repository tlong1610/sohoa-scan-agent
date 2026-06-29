using Sohoa.ScanAgent.Services;

namespace Sohoa.ScanAgent.Api.Routes;

public record ScanRequest(
    string? UploadUrl = null,
    string[]? UploadUrls = null,
    bool? ShowUi = null,
    int? Dpi = null,
    string? ColorMode = null,
    string? TwainSource = null,
    bool? Adf = null,
    bool? Duplex = null);

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

            var duplex = req.Duplex ?? false;
            var adf = req.Adf ?? duplex;
            var batchMode = adf || duplex;
            var timeout = batchMode ? TimeSpan.FromSeconds(610) : TimeSpan.FromSeconds(130);

            var tcs = new TaskCompletionSource<ScanOutcome>();

            invoker.BeginInvoke(() =>
            {
                try
                {
                    var pages = twain.ScanPagesJpeg(
                        req.ShowUi ?? !batchMode,
                        req.Dpi ?? 300,
                        req.ColorMode ?? "bw",
                        req.TwainSource,
                        adf,
                        duplex,
                        hwnd.Handle);

                    if (pages is null || pages.Count == 0)
                    {
                        tcs.TrySetResult(ScanOutcome.FromCancelled());
                        return;
                    }

                    var uploadUrls = ResolveUploadUrls(req, pages.Count);
                    if (uploadUrls is not null)
                    {
                        twain.UploadJpegBatchAsync(uploadUrls, pages)
                            .ContinueWith(t =>
                            {
                                if (t.IsFaulted)
                                    tcs.TrySetException(t.Exception!.GetBaseException());
                                else
                                    tcs.TrySetResult(new ScanOutcome(pages.Count, true));
                            });
                        return;
                    }

                    tcs.TrySetResult(new ScanOutcome(pages[0], pages.Count));
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });

            var result = await tcs.Task.WaitAsync(timeout);

            if (result.IsCancelled)
                return Results.Ok(new { ok = true, cancelled = true });

            if (result.Uploaded)
                return Results.Ok(new { ok = true, uploaded = true, pageCount = result.PageCount });

            return Results.File(result.Jpeg!, "image/jpeg");
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message);
        }
    }

    private static string[]? ResolveUploadUrls(ScanRequest req, int pageCount)
    {
        if (req.UploadUrls is { Length: > 0 })
            return req.UploadUrls;

        if (!string.IsNullOrWhiteSpace(req.UploadUrl))
            return Enumerable.Repeat(req.UploadUrl, pageCount).ToArray();

        return null;
    }

    private sealed record ScanOutcome
    {
        public bool IsCancelled { get; init; }
        public bool Uploaded { get; init; }
        public int PageCount { get; init; }
        public byte[]? Jpeg { get; init; }

        public static ScanOutcome FromCancelled() =>
            new(Array.Empty<byte>()) { IsCancelled = true, PageCount = 0 };

        public ScanOutcome(byte[] jpeg, int pageCount = 1)
        {
            Jpeg = jpeg;
            PageCount = pageCount;
        }

        public ScanOutcome(int pageCount, bool uploaded)
        {
            PageCount = pageCount;
            Uploaded = uploaded;
        }
    }
}
