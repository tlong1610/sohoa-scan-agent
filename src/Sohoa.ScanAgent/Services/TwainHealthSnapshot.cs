namespace Sohoa.ScanAgent.Services;

public sealed record TwainHealthSnapshot(
    IReadOnlyList<string> Sources,
    string? TwainError,
    DateTime CachedAtUtc)
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public bool IsStale => CachedAtUtc == DateTime.MinValue
        || DateTime.UtcNow - CachedAtUtc > CacheTtl;
}
