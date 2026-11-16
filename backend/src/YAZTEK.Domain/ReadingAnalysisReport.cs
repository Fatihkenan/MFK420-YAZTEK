namespace YAZTEK.Domain;

public sealed class ReadingAnalysisReport
{
    public Guid Id { get; set; }
    public required string StudentId { get; set; }
    public required string TextId { get; set; }
    public required string ReferenceTextHash { get; set; }
    public string? Transcript { get; set; }
    public required string ModelId { get; set; }
    public required string AnalysisJson { get; set; }
    public bool ConsentGranted { get; set; }
    public bool AudioRetained { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
}
