using System.Text.Json.Serialization;

namespace Sohoa.ScanAgent.Core.Models;

public enum ExportStatus { Draft, Exported }
public enum SessionStatus { Draft, Committed }

public record PageMeta
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string TiffPath { get; set; } = "";
    public int Rotation { get; set; } = 0;           // 0, 90, 180, 270
    public CropRect? Crop { get; set; } = null;
    public int SortOrder { get; set; } = 0;
    public DateTime ScannedAt { get; init; } = DateTime.UtcNow;
}

public record CropRect
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

public record ScanDocument
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public int SortOrder { get; set; } = 0;
    public List<string> PageIds { get; set; } = new();

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ExportStatus ExportStatus { get; set; } = ExportStatus.Draft;

    public string? ExportedPdfPath { get; set; } = null;
}

public record ScanDossier
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public int SortOrder { get; set; } = 0;
    public List<ScanDocument> Documents { get; set; } = new();
}

public record ScanSession
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string? ProjectCode { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SessionStatus Status { get; set; } = SessionStatus.Draft;

    public List<ScanDossier> Dossiers { get; set; } = new();

    // in-memory page lookup (not serialised)
    [JsonIgnore]
    public Dictionary<string, PageMeta> Pages { get; set; } = new();
}

public record SessionIndex
{
    public List<string> SessionIds { get; set; } = new();
}
