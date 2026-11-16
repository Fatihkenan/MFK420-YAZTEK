namespace YAZTEK.Infrastructure.Configuration;

public sealed class ComprehensionAiOptions
{
    public const string SectionName = "ComprehensionAi";
    public string Provider { get; set; } = "Ollama";
    public string BaseUrl { get; set; } = "http://127.0.0.1:11434/";
    public string Model { get; set; } = "qwen3:4b";
    public int TimeoutSeconds { get; set; } = 300;
    public int MaxTransientRetries { get; set; } = 1;
    public int NumContext { get; set; } = 16_384;
    public int NumPredict { get; set; } = 1_536;
    public int QuestionBatchSize { get; set; } = 2;
    public int MaxOutputRetries { get; set; } = 2;
    public int QualityReviewBatchSize { get; set; } = 4;
    public int QualityReviewNumPredict { get; set; } = 768;
    public int EvaluationNumPredict { get; set; } = 1_536;
    public string KeepAlive { get; set; } = "30m";
}
