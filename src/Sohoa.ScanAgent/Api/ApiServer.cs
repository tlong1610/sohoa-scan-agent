using Sohoa.ScanAgent.Api.Routes;
using Sohoa.ScanAgent.Services;

namespace Sohoa.ScanAgent.Api;

public static class ApiServer
{
    public static async Task RunAsync(
        TwainService twain,
        IntPtr windowHandle,
        CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateBuilder();

        builder.WebHost.UseUrls("http://127.0.0.1:18612");
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxConcurrentConnections = 4;
        });

        builder.Services.AddSingleton(twain);
        builder.Services.AddSingleton(new WindowHandleAccessor(windowHandle));
        builder.Services.AddCors(opts =>
        {
            opts.AddDefaultPolicy(p => p
                .SetIsOriginAllowed(_ => true)
                .AllowAnyMethod()
                .AllowAnyHeader());
        });

        var app = builder.Build();
        app.UseCors();

        HealthRoutes.Map(app);
        ScanRoutes.Map(app);

        await app.RunAsync(cancellationToken);
    }
}

public record WindowHandleAccessor(IntPtr Handle);
