using Sohoa.ScanAgent.Services;

namespace Sohoa.ScanAgent.Api.Routes;

public static class HealthRoutes
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/health", (TwainService twain, WindowHandleAccessor hwnd, bool? refresh) =>
        {
            if (refresh == true)
                twain.QueueRefreshSources(hwnd.Handle);
            else
            {
                var snapshot = twain.GetHealthSnapshot();
                if (snapshot.IsStale && !twain.IsRefreshInFlight)
                    twain.QueueRefreshSources(hwnd.Handle);
            }

            var health = twain.GetHealthSnapshot();
            var sources = health.Sources;
            var twainError = health.TwainError;

            var bitness = Environment.Is64BitProcess ? "x64" : "x86";
            string? twainHint = null;
            if (sources.Count == 0 && bitness == "x64")
            {
                twainHint =
                    "No TWAIN sources visible to 64-bit process. Plustek PS4080U uses 32-bit TWAIN — download SohoaScanAgent win-x86 build.";
            }

            return Results.Ok(new
            {
                status = "ok",
                version = "2.0.5",
                agent = "Sohoa Scan Agent",
                processBitness = bitness,
                twainSources = sources,
                twainError,
                twainHint,
                sourcesCachedAt = health.CachedAtUtc == DateTime.MinValue
                    ? (DateTime?)null
                    : health.CachedAtUtc,
            });
        });
    }
}
