using Sohoa.ScanAgent.Core.Models;
using Sohoa.ScanAgent.Core.Services;
using Sohoa.ScanAgent.Services;

namespace Sohoa.ScanAgent.Api.Routes;

public static class DocumentRoutes
{
    public static void Map(WebApplication app)
    {
        // Add document to dossier
        app.MapPost("/sessions/{sessionId}/dossiers/{dossierId}/documents", (
            string sessionId,
            string dossierId,
            CreateDocumentRequest req,
            StagingService staging) =>
        {
            try
            {
                var doc = staging.AddDocument(sessionId, dossierId, req.Name);
                return Results.Created($"/documents/{doc.Id}", doc);
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
        });

        // Rename / reorder document
        app.MapPatch("/documents/{documentId}", (
            string documentId,
            UpdateDocumentRequest req,
            StagingService staging,
            HttpContext ctx) =>
        {
            var sessionId = ctx.Request.Query["sessionId"].ToString();
            var dossierId = ctx.Request.Query["dossierId"].ToString();
            if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(dossierId))
                return Results.BadRequest("sessionId and dossierId query params required");
            try
            {
                var doc = staging.UpdateDocument(sessionId, dossierId, documentId, req.Name, req.SortOrder);
                return Results.Ok(doc);
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
        });

        // Delete document + pages
        app.MapDelete("/sessions/{sessionId}/dossiers/{dossierId}/documents/{documentId}", (
            string sessionId,
            string dossierId,
            string documentId,
            StagingService staging) =>
        {
            try
            {
                staging.DeleteDocument(sessionId, dossierId, documentId);
                return Results.NoContent();
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
        });

        // Scan one page into document (calls TWAIN on UI thread via dispatcher)
        app.MapPost("/documents/{documentId}/scan", async (
            string documentId,
            ScanRequest req,
            StagingService staging,
            TwainService twain,
            WindowHandleAccessor hwnd,
            HttpContext ctx) =>
        {
            var sessionId = ctx.Request.Query["sessionId"].ToString();
            var dossierId = ctx.Request.Query["dossierId"].ToString();
            if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(dossierId))
                return Results.BadRequest("sessionId and dossierId query params required");

            try
            {
                var invoker = ScanAgentApp.UiInvoker;
                if (invoker is null || invoker.IsDisposed)
                    return Results.Problem("Scan Agent UI is not ready. Restart SohoaScanAgent.exe.");

                PageMeta? page = null;
                var tcs = new TaskCompletionSource<PageMeta?>();

                // TWAIN must run on the STA WinForms thread with an active message pump
                invoker.BeginInvoke(() =>
                {
                    try
                    {
                        var result = twain.ScanOnePage(
                            sessionId, dossierId, documentId,
                            req.ShowUi ?? true,
                            req.Dpi ?? 300,
                            req.ColorMode ?? "bw",
                            hwnd.Handle);
                        tcs.TrySetResult(result);
                    }
                    catch (Exception ex) { tcs.TrySetException(ex); }
                });

                page = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(130));
                if (page is null) return Results.Ok(new { cancelled = true });
                return Results.Ok(page);
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (Exception ex) { return Results.Problem(ex.Message); }
        });

        // Export document pages → PDF (local staging only)
        app.MapPost("/documents/{documentId}/export", async (
            string documentId,
            StagingService staging,
            HttpContext ctx) =>
        {
            var sessionId = ctx.Request.Query["sessionId"].ToString();
            var dossierId = ctx.Request.Query["dossierId"].ToString();
            if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(dossierId))
                return Results.BadRequest("sessionId and dossierId query params required");

            var session = staging.GetSession(sessionId);
            if (session is null) return Results.NotFound("Session not found");

            var dossier = session.Dossiers.FirstOrDefault(d => d.Id == dossierId);
            if (dossier is null) return Results.NotFound("Dossier not found");

            var doc = dossier.Documents.FirstOrDefault(d => d.Id == documentId);
            if (doc is null) return Results.NotFound("Document not found");

            var pages = staging.GetOrderedPages(session, doc);
            if (pages.Count == 0) return Results.BadRequest("Document has no pages");

            var pdfPath = staging.GetExportPath(sessionId, dossierId, documentId);
            await Task.Run(() => PdfExportService.ExportToPdf(pages, pdfPath));

            staging.MarkDocumentExported(sessionId, dossierId, documentId, pdfPath);
            return Results.Ok(new { documentId, pdfPath, pageCount = pages.Count });
        });

        // Download exported PDF bytes (for frontend upload to data-lake)
        app.MapGet("/documents/{documentId}/pdf", (
            string documentId,
            StagingService staging,
            HttpContext ctx) =>
        {
            var sessionId = ctx.Request.Query["sessionId"].ToString();
            var dossierId = ctx.Request.Query["dossierId"].ToString();
            if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(dossierId))
                return Results.BadRequest("sessionId and dossierId query params required");

            var session = staging.GetSession(sessionId);
            if (session is null) return Results.NotFound("Session not found");

            var dossier = session.Dossiers.FirstOrDefault(d => d.Id == dossierId);
            if (dossier is null) return Results.NotFound("Dossier not found");

            var doc = dossier.Documents.FirstOrDefault(d => d.Id == documentId);
            if (doc is null) return Results.NotFound("Document not found");

            var pdfPath = staging.GetExportPath(sessionId, dossierId, documentId);
            if (!File.Exists(pdfPath))
                return Results.NotFound("PDF not exported yet");

            return Results.File(pdfPath, "application/pdf", $"{doc.Name}.pdf");
        });
    }
}

public record CreateDocumentRequest(string Name);
public record UpdateDocumentRequest(string? Name, int? SortOrder);
public record ScanRequest(bool? ShowUi, int? Dpi, string? ColorMode);
