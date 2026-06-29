using Sohoa.ScanAgent.Services;

namespace Sohoa.ScanAgent.Api.Routes;

public static class HealthRoutes
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/health", (TwainService twain) =>
        {
            List<string> sources;
            string? twainError = null;
            try
            {
                sources = twain.GetSources();
            }
            catch (Exception ex)
            {
                sources = [];
                twainError = ex.Message;
            }

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
                version = "2.0.1",
                agent = "Sohoa Scan Agent",
                processBitness = bitness,
                twainSources = sources,
                twainError,
                twainHint,
            });
        });
    }
}
