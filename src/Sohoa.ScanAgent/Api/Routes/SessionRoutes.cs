using Sohoa.ScanAgent.Core.Services;

namespace Sohoa.ScanAgent.Api.Routes;

public static class SessionRoutes
{
    public static void Map(WebApplication app)
    {
        // List all draft sessions
        app.MapGet("/sessions", (StagingService staging) =>
        {
            var sessions = staging.ListSessions();
            return Results.Ok(sessions);
        });

        // Get single session
        app.MapGet("/sessions/{sessionId}", (string sessionId, StagingService staging) =>
        {
            var session = staging.GetSession(sessionId);
            return session is null ? Results.NotFound() : Results.Ok(session);
        });

        // Create session
        app.MapPost("/sessions", (CreateSessionRequest req, StagingService staging) =>
        {
            var session = staging.CreateSession(req.ProjectCode);
            return Results.Created($"/sessions/{session.Id}", session);
        });

        // Delete session (after commit or cancel)
        app.MapDelete("/sessions/{sessionId}", (string sessionId, StagingService staging) =>
        {
            var deleted = staging.DeleteSession(sessionId);
            return deleted ? Results.NoContent() : Results.NotFound();
        });
    }
}

public record CreateSessionRequest(string? ProjectCode);
