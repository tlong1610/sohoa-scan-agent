using Sohoa.ScanAgent.Core.Models;
using Sohoa.ScanAgent.Core.Services;

namespace Sohoa.ScanAgent.Api.Routes;

public static class PageRoutes
{
    public static void Map(WebApplication app)
    {
        // Preview thumbnail (JPEG, max 600px)
        app.MapGet("/pages/{pageId}/preview", (
            string pageId,
            StagingService staging,
            HttpContext ctx) =>
        {
            var sessionId = ctx.Request.Query["sessionId"].ToString();
            if (string.IsNullOrEmpty(sessionId)) return Results.BadRequest("sessionId required");

            var page = staging.GetPage(sessionId, pageId);
            if (page is null || !File.Exists(page.TiffPath)) return Results.NotFound();

            var jpegBytes = ImageProcessingService.GetPreviewJpeg(page.TiffPath, page, maxDimension: 600);
            return Results.Bytes(jpegBytes, "image/jpeg");
        });

        // Rotate page
        app.MapPost("/pages/{pageId}/rotate", (
            string pageId,
            RotateRequest req,
            StagingService staging,
            HttpContext ctx) =>
        {
            var sessionId = ctx.Request.Query["sessionId"].ToString();
            var dossierId = ctx.Request.Query["dossierId"].ToString();
            var documentId = ctx.Request.Query["documentId"].ToString();
            if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(dossierId) || string.IsNullOrEmpty(documentId))
                return Results.BadRequest("sessionId, dossierId, documentId required");

            if (req.Degrees is not (90 or 180 or 270))
                return Results.BadRequest("degrees must be 90, 180, or 270");

            try
            {
                var page = staging.UpdatePageRotation(sessionId, dossierId, documentId, pageId, req.Degrees);
                return Results.Ok(page);
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
        });

        // Crop page
        app.MapPost("/pages/{pageId}/crop", (
            string pageId,
            CropRect req,
            StagingService staging,
            HttpContext ctx) =>
        {
            var sessionId = ctx.Request.Query["sessionId"].ToString();
            var dossierId = ctx.Request.Query["dossierId"].ToString();
            var documentId = ctx.Request.Query["documentId"].ToString();
            if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(dossierId) || string.IsNullOrEmpty(documentId))
                return Results.BadRequest("sessionId, dossierId, documentId required");

            try
            {
                var page = staging.UpdatePageCrop(sessionId, dossierId, documentId, pageId, req);
                return Results.Ok(page);
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
        });

        // Reorder page within document
        app.MapPatch("/pages/{pageId}", (
            string pageId,
            ReorderPageRequest req,
            StagingService staging,
            HttpContext ctx) =>
        {
            var sessionId = ctx.Request.Query["sessionId"].ToString();
            var dossierId = ctx.Request.Query["dossierId"].ToString();
            var documentId = ctx.Request.Query["documentId"].ToString();
            if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(dossierId) || string.IsNullOrEmpty(documentId))
                return Results.BadRequest("sessionId, dossierId, documentId required");

            try
            {
                var page = staging.UpdatePageOrder(sessionId, dossierId, documentId, pageId, req.SortOrder);
                return Results.Ok(page);
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
        });

        // Delete page
        app.MapDelete("/pages/{pageId}", (
            string pageId,
            StagingService staging,
            HttpContext ctx) =>
        {
            var sessionId = ctx.Request.Query["sessionId"].ToString();
            var dossierId = ctx.Request.Query["dossierId"].ToString();
            var documentId = ctx.Request.Query["documentId"].ToString();
            if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(dossierId) || string.IsNullOrEmpty(documentId))
                return Results.BadRequest("sessionId, dossierId, documentId required");

            try
            {
                staging.DeletePage(sessionId, dossierId, documentId, pageId);
                return Results.NoContent();
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
        });
    }
}

public record RotateRequest(int Degrees);
public record ReorderPageRequest(int SortOrder);
