using Sohoa.ScanAgent.Api.Routes;
using Sohoa.ScanAgent.Core.Services;
using Sohoa.ScanAgent.Services;

namespace Sohoa.ScanAgent.Api;

/// <summary>
/// Hosts ASP.NET Core Minimal API on http://127.0.0.1:18612
/// Runs on a background thread; the STA WinForms thread owns TWAIN.
/// </summary>
public static class ApiServer
{
    public static async Task RunAsync(
        StagingService staging,
        TwainService twain,
        IntPtr windowHandle,
        CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateBuilder();

        builder.WebHost.UseUrls("http://127.0.0.1:18612");
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.Services.AddSingleton(staging);
        builder.Services.AddSingleton(twain);
        builder.Services.AddSingleton(new WindowHandleAccessor(windowHandle));
        builder.Services.AddCors(opts =>
        {
            opts.AddDefaultPolicy(p => p
                .SetIsOriginAllowed(_ => true)   // allows any origin (localhost + app domain)
                .AllowAnyMethod()
                .AllowAnyHeader());
        });

        var app = builder.Build();
        app.UseCors();

        HealthRoutes.Map(app);
        SessionRoutes.Map(app);
        DossierRoutes.Map(app);
        DocumentRoutes.Map(app);
        PageRoutes.Map(app);

        await app.RunAsync(cancellationToken);
    }
}

public record WindowHandleAccessor(IntPtr Handle);
