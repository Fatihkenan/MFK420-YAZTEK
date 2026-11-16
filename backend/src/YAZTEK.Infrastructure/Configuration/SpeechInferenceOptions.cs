namespace YAZTEK.Infrastructure.Configuration;

public sealed class SpeechInferenceOptions
{
    public const string SectionName = "SpeechInference";

    public string BaseUrl { get; init; } = "http://127.0.0.1:8091";
    public int TimeoutSeconds { get; init; } = 300;
    public bool AutoStartLocal { get; init; }
    public int StartupTimeoutSeconds { get; init; } = 90;
}
