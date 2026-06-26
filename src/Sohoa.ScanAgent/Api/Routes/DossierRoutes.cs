using Sohoa.ScanAgent.Core.Services;

namespace Sohoa.ScanAgent.Api.Routes;

public static class DossierRoutes
{
    public static void Map(WebApplication app)
    {
        // Add dossier to session
        app.MapPost("/sessions/{sessionId}/dossiers", (
            string sessionId,
            CreateDossierRequest req,
            StagingService staging) =>
        {
            try
            {
                var dossier = staging.AddDossier(sessionId, req.Name);
                return Results.Created($"/sessions/{sessionId}/dossiers/{dossier.Id}", dossier);
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
        });

        // Rename / reorder dossier
        app.MapPatch("/dossiers/{dossierId}", (
            string dossierId,
            UpdateDossierRequest req,
            StagingService staging,
            HttpContext ctx) =>
        {
            var sessionId = ctx.Request.Query["sessionId"].ToString();
            if (string.IsNullOrEmpty(sessionId)) return Results.BadRequest("sessionId query param required");
            try
            {
                var dossier = staging.UpdateDossier(sessionId, dossierId, req.Name, req.SortOrder);
                return Results.Ok(dossier);
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
        });

        // Delete dossier + all documents
        app.MapDelete("/sessions/{sessionId}/dossiers/{dossierId}", (
            string sessionId,
            string dossierId,
            StagingService staging) =>
        {
            try
            {
                staging.DeleteDossier(sessionId, dossierId);
                return Results.NoContent();
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
        });

        // List exported PDFs for a dossier
        app.MapGet("/sessions/{sessionId}/dossiers/{dossierId}/exports", (
            string sessionId,
            string dossierId,
            StagingService staging) =>
        {
            var session = staging.GetSession(sessionId);
            if (session is null) return Results.NotFound();
            var dossier = session.Dossiers.FirstOrDefault(d => d.Id == dossierId);
            if (dossier is null) return Results.NotFound();

            var exported = dossier.Documents
                .Where(d => d.ExportStatus == Core.Models.ExportStatus.Exported)
                .Select(d => new { d.Id, d.Name, d.ExportedPdfPath });

            return Results.Ok(exported);
        });
    }
}

public record CreateDossierRequest(string Name);
public record UpdateDossierRequest(string? Name, int? SortOrder);
