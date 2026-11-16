namespace YAZTEK.Application.Speech;

public sealed record SpeechInferenceResult(int StatusCode, bool IsSuccess, string Body);

public interface ISpeechInferenceClient
{
    Task<SpeechInferenceResult> AnalyzeAsync(
        Stream audio,
        string fileName,
        string contentType,
        string referenceText,
        bool consent,
        CancellationToken cancellationToken);

    Task<SpeechInferenceResult> GetHealthAsync(CancellationToken cancellationToken);
}
