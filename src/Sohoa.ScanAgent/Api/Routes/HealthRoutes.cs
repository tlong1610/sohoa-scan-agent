using Sohoa.ScanAgent.Services;

namespace Sohoa.ScanAgent.Api.Routes;

public static class HealthRoutes
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/health", (TwainService twain) =>
        {
            List<string> sources;
            try
            {
                sources = twain.GetSources();
            }
            catch
            {
                sources = [];
            }

            return Results.Ok(new
            {
                status = "ok",
                version = "1.0.2",
                agent = "Sohoa Scan Agent",
                twainSources = sources,
            });
        });
    }
}
