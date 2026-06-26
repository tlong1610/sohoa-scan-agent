namespace Sohoa.ScanAgent.Api.Routes;

public static class HealthRoutes
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/health", () => Results.Ok(new
        {
            status = "ok",
            version = "1.0.0",
            agent = "Sohoa Scan Agent"
        }));
    }
}
