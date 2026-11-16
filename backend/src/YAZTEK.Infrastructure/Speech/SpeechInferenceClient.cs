using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using YAZTEK.Application.Speech;
using YAZTEK.Infrastructure.Configuration;

namespace YAZTEK.Infrastructure.Speech;

public sealed class SpeechInferenceClient(
    HttpClient httpClient,
    IOptions<SpeechInferenceOptions> options) : ISpeechInferenceClient
{
    private readonly SpeechInferenceOptions _options = options.Value;

    public async Task<SpeechInferenceResult> AnalyzeAsync(
        Stream audio,
        string fileName,
        string contentType,
        string referenceText,
        bool consent,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "analyze");
        using var multipart = new MultipartFormDataContent();
        using var streamContent = new StreamContent(audio);
        streamContent.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        multipart.Add(streamContent, "audio", Path.GetFileName(fileName));
        multipart.Add(new StringContent(referenceText), "reference_text");
        multipart.Add(new StringContent(consent ? "true" : "false"), "consent");
        request.Content = multipart;
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return new SpeechInferenceResult((int)response.StatusCode, response.IsSuccessStatusCode, body);
    }

    public async Task<SpeechInferenceResult> GetHealthAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync("health", cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return new SpeechInferenceResult((int)response.StatusCode, response.IsSuccessStatusCode, body);
    }
}
