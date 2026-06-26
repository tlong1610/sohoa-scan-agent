using System.Text.Json;
using System.Text.Json.Serialization;
using Sohoa.ScanAgent.Core.Models;

namespace Sohoa.ScanAgent.Core.Services;

/// <summary>
/// Manages persistent staging: sessions, dossiers, documents, pages.
/// All state lives under %AppData%\SohoaScanAgent\sessions\{sessionId}\
/// </summary>
public class StagingService
{
    private readonly string _root;
    private readonly JsonSerializerOptions _jsonOpts;
    private readonly Dictionary<string, ScanSession> _cache = new();

    public StagingService()
    {
        _root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SohoaScanAgent", "sessions");
        Directory.CreateDirectory(_root);

        _jsonOpts = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };
    }

    // ─── Session ──────────────────────────────────────────────────────────────

    public List<ScanSession> ListSessions()
    {
        var sessions = new List<ScanSession>();
        if (!Directory.Exists(_root)) return sessions;
        foreach (var dir in Directory.GetDirectories(_root))
        {
            var manifestPath = Path.Combine(dir, "manifest.json");
            if (!File.Exists(manifestPath)) continue;
            var session = LoadSession(Path.GetFileName(dir));
            if (session != null) sessions.Add(session);
        }
        return sessions.OrderByDescending(s => s.CreatedAt).ToList();
    }

    public ScanSession CreateSession(string? projectCode)
    {
        var session = new ScanSession { ProjectCode = projectCode };
        session.Pages = new Dictionary<string, PageMeta>();
        PersistSession(session);
        return session;
    }

    public ScanSession? GetSession(string sessionId)
    {
        if (_cache.TryGetValue(sessionId, out var cached)) return cached;
        return LoadSession(sessionId);
    }

    public void SaveSession(ScanSession session)
    {
        session.UpdatedAt = DateTime.UtcNow;
        _cache[session.Id] = session;
        PersistSession(session);
    }

    public bool DeleteSession(string sessionId)
    {
        var dir = SessionDir(sessionId);
        if (!Directory.Exists(dir)) return false;
        Directory.Delete(dir, recursive: true);
        _cache.Remove(sessionId);
        return true;
    }

    // ─── Dossier ──────────────────────────────────────────────────────────────

    public ScanDossier AddDossier(string sessionId, string name)
    {
        var session = GetSession(sessionId) ?? throw new KeyNotFoundException($"Session {sessionId} not found");
        var dossier = new ScanDossier { Name = name, SortOrder = session.Dossiers.Count };
        session.Dossiers.Add(dossier);
        EnsureDossierDirs(sessionId, dossier.Id);
        SaveSession(session);
        return dossier;
    }

    public ScanDossier UpdateDossier(string sessionId, string dossierId, string? name, int? sortOrder)
    {
        var session = GetSession(sessionId) ?? throw new KeyNotFoundException();
        var dossier = FindDossier(session, dossierId);
        if (name != null) dossier.Name = name;
        if (sortOrder.HasValue) dossier.SortOrder = sortOrder.Value;
        SaveSession(session);
        return dossier;
    }

    public bool DeleteDossier(string sessionId, string dossierId)
    {
        var session = GetSession(sessionId) ?? throw new KeyNotFoundException();
        var dossier = FindDossier(session, dossierId);
        session.Dossiers.Remove(dossier);
        var dir = DossierDir(sessionId, dossierId);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        SaveSession(session);
        return true;
    }

    // ─── Document ─────────────────────────────────────────────────────────────

    public ScanDocument AddDocument(string sessionId, string dossierId, string name)
    {
        var session = GetSession(sessionId) ?? throw new KeyNotFoundException();
        var dossier = FindDossier(session, dossierId);
        var doc = new ScanDocument { Name = name, SortOrder = dossier.Documents.Count };
        dossier.Documents.Add(doc);
        EnsureDocumentDirs(sessionId, dossierId, doc.Id);
        SaveSession(session);
        return doc;
    }

    public ScanDocument UpdateDocument(string sessionId, string dossierId, string documentId, string? name, int? sortOrder)
    {
        var session = GetSession(sessionId) ?? throw new KeyNotFoundException();
        var (_, doc) = FindDocument(session, dossierId, documentId);
        if (name != null) doc.Name = name;
        if (sortOrder.HasValue) doc.SortOrder = sortOrder.Value;
        SaveSession(session);
        return doc;
    }

    public bool DeleteDocument(string sessionId, string dossierId, string documentId)
    {
        var session = GetSession(sessionId) ?? throw new KeyNotFoundException();
        var (dossier, doc) = FindDocument(session, dossierId, documentId);
        dossier.Documents.Remove(doc);
        var dir = DocumentDir(sessionId, dossierId, documentId);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        SaveSession(session);
        return true;
    }

    public ScanDocument MarkDocumentExported(string sessionId, string dossierId, string documentId, string pdfPath)
    {
        var session = GetSession(sessionId) ?? throw new KeyNotFoundException();
        var (_, doc) = FindDocument(session, dossierId, documentId);
        doc.ExportStatus = ExportStatus.Exported;
        doc.ExportedPdfPath = pdfPath;
        SaveSession(session);
        return doc;
    }

    // ─── Page ─────────────────────────────────────────────────────────────────

    public PageMeta AddPage(string sessionId, string dossierId, string documentId, string tiffPath)
    {
        var session = GetSession(sessionId) ?? throw new KeyNotFoundException();
        var (_, doc) = FindDocument(session, dossierId, documentId);
        var page = new PageMeta { TiffPath = tiffPath, SortOrder = doc.PageIds.Count };
        doc.PageIds.Add(page.Id);
        session.Pages[page.Id] = page;
        PersistPageMeta(sessionId, dossierId, documentId, page);
        SaveSession(session);
        return page;
    }

    public PageMeta? GetPage(string sessionId, string pageId)
    {
        var session = GetSession(sessionId);
        if (session == null) return null;
        return session.Pages.TryGetValue(pageId, out var p) ? p : null;
    }

    public PageMeta UpdatePageRotation(string sessionId, string dossierId, string documentId, string pageId, int degrees)
    {
        var session = GetSession(sessionId) ?? throw new KeyNotFoundException();
        if (!session.Pages.TryGetValue(pageId, out var page)) throw new KeyNotFoundException($"Page {pageId}");
        page.Rotation = (page.Rotation + degrees) % 360;
        if (page.Rotation < 0) page.Rotation += 360;
        PersistPageMeta(sessionId, dossierId, documentId, page);
        SaveSession(session);
        return page;
    }

    public PageMeta UpdatePageCrop(string sessionId, string dossierId, string documentId, string pageId, CropRect crop)
    {
        var session = GetSession(sessionId) ?? throw new KeyNotFoundException();
        if (!session.Pages.TryGetValue(pageId, out var page)) throw new KeyNotFoundException($"Page {pageId}");
        page.Crop = crop;
        PersistPageMeta(sessionId, dossierId, documentId, page);
        SaveSession(session);
        return page;
    }

    public PageMeta UpdatePageOrder(string sessionId, string dossierId, string documentId, string pageId, int sortOrder)
    {
        var session = GetSession(sessionId) ?? throw new KeyNotFoundException();
        var (_, doc) = FindDocument(session, dossierId, documentId);
        if (!session.Pages.TryGetValue(pageId, out var page)) throw new KeyNotFoundException($"Page {pageId}");
        doc.PageIds.Remove(pageId);
        var clampedIndex = Math.Clamp(sortOrder, 0, doc.PageIds.Count);
        doc.PageIds.Insert(clampedIndex, pageId);
        page.SortOrder = sortOrder;
        SaveSession(session);
        return page;
    }

    public bool DeletePage(string sessionId, string dossierId, string documentId, string pageId)
    {
        var session = GetSession(sessionId) ?? throw new KeyNotFoundException();
        var (_, doc) = FindDocument(session, dossierId, documentId);
        doc.PageIds.Remove(pageId);
        if (session.Pages.TryGetValue(pageId, out var page))
        {
            session.Pages.Remove(pageId);
            if (File.Exists(page.TiffPath)) File.Delete(page.TiffPath);
            var metaPath = page.TiffPath.Replace(".tiff", ".meta.json");
            if (File.Exists(metaPath)) File.Delete(metaPath);
        }
        doc.ExportStatus = ExportStatus.Draft;
        doc.ExportedPdfPath = null;
        SaveSession(session);
        return true;
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    public string GetPageTiffPath(string sessionId, string dossierId, string documentId, string pageId)
        => Path.Combine(DocumentPagesDir(sessionId, dossierId, documentId), $"{pageId}.tiff");

    public string GetExportPath(string sessionId, string dossierId, string documentId)
        => Path.Combine(DossierExportsDir(sessionId, dossierId), $"{documentId}.pdf");

    public List<PageMeta> GetOrderedPages(ScanSession session, ScanDocument doc)
    {
        return doc.PageIds
            .Select(id => session.Pages.TryGetValue(id, out var p) ? p : null)
            .Where(p => p != null)
            .Select(p => p!)
            .ToList();
    }

    // ─── Path helpers ─────────────────────────────────────────────────────────

    public string SessionDir(string sessionId) => Path.Combine(_root, sessionId);
    public string DossierDir(string sessionId, string dossierId) => Path.Combine(SessionDir(sessionId), "dossiers", dossierId);
    public string DocumentDir(string sessionId, string dossierId, string documentId) => Path.Combine(DossierDir(sessionId, dossierId), "documents", documentId);
    public string DocumentPagesDir(string sessionId, string dossierId, string documentId) => Path.Combine(DocumentDir(sessionId, dossierId, documentId), "pages");
    public string DossierExportsDir(string sessionId, string dossierId) => Path.Combine(DossierDir(sessionId, dossierId), "exports");

    // ─── Private ──────────────────────────────────────────────────────────────

    private ScanSession? LoadSession(string sessionId)
    {
        var manifestPath = Path.Combine(SessionDir(sessionId), "manifest.json");
        if (!File.Exists(manifestPath)) return null;
        var json = File.ReadAllText(manifestPath);
        var session = JsonSerializer.Deserialize<ScanSession>(json, _jsonOpts);
        if (session == null) return null;

        // Re-hydrate pages from disk
        session.Pages = new Dictionary<string, PageMeta>();
        foreach (var dossier in session.Dossiers)
            foreach (var doc in dossier.Documents)
                foreach (var pageId in doc.PageIds)
                {
                    var metaPath = Path.Combine(DocumentPagesDir(sessionId, dossier.Id, doc.Id), $"{pageId}.meta.json");
                    if (!File.Exists(metaPath)) continue;
                    var pageMeta = JsonSerializer.Deserialize<PageMeta>(File.ReadAllText(metaPath), _jsonOpts);
                    if (pageMeta != null) session.Pages[pageId] = pageMeta;
                }

        _cache[sessionId] = session;
        return session;
    }

    private void PersistSession(ScanSession session)
    {
        var dir = SessionDir(session.Id);
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(session, _jsonOpts);
        File.WriteAllText(Path.Combine(dir, "manifest.json"), json);
    }

    private void PersistPageMeta(string sessionId, string dossierId, string documentId, PageMeta page)
    {
        var dir = DocumentPagesDir(sessionId, dossierId, documentId);
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(page, _jsonOpts);
        File.WriteAllText(Path.Combine(dir, $"{page.Id}.meta.json"), json);
    }

    private void EnsureDossierDirs(string sessionId, string dossierId)
    {
        Directory.CreateDirectory(DossierDir(sessionId, dossierId));
        Directory.CreateDirectory(DossierExportsDir(sessionId, dossierId));
    }

    private void EnsureDocumentDirs(string sessionId, string dossierId, string documentId)
    {
        Directory.CreateDirectory(DocumentPagesDir(sessionId, dossierId, documentId));
    }

    private static ScanDossier FindDossier(ScanSession session, string dossierId)
        => session.Dossiers.FirstOrDefault(d => d.Id == dossierId)
           ?? throw new KeyNotFoundException($"Dossier {dossierId} not found");

    private static (ScanDossier dossier, ScanDocument doc) FindDocument(ScanSession session, string dossierId, string documentId)
    {
        var dossier = FindDossier(session, dossierId);
        var doc = dossier.Documents.FirstOrDefault(d => d.Id == documentId)
                  ?? throw new KeyNotFoundException($"Document {documentId} not found");
        return (dossier, doc);
    }
}
