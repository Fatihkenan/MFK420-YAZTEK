namespace YAZTEK.Infrastructure.Configuration;

public sealed class AnalysisOptions
{
    public const string SectionName = "Analysis";

    public bool PersistReports { get; init; }
    public bool RequireAuthentication { get; init; }
    public bool RequireConsent { get; init; } = true;
    public long MaxUploadBytes { get; init; } = 25 * 1024 * 1024;
    public int RetentionDays { get; init; } = 30;
    public string[] AllowedOrigins { get; init; } = ["http://localhost:5173", "http://127.0.0.1:5173"];
}
