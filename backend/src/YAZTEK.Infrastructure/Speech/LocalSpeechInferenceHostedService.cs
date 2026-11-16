using System.Diagnostics;
using System.Net.Http.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YAZTEK.Infrastructure.Configuration;

namespace YAZTEK.Infrastructure.Speech;

public sealed class LocalSpeechInferenceHostedService(
    IHostEnvironment environment,
    IOptions<SpeechInferenceOptions> options,
    ILogger<LocalSpeechInferenceHostedService> logger) : IHostedService, IDisposable
{
    private readonly SpeechInferenceOptions _options = options.Value;
    private Process? _process;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.AutoStartLocal)
        {
            return;
        }

        var baseUri = new Uri(_options.BaseUrl);
        if (!baseUri.IsLoopback)
        {
            logger.LogWarning("Yerel ses servisi otomatik başlatılmadı; BaseUrl loopback değil: {BaseUrl}", _options.BaseUrl);
            return;
        }

        if (await IsReadyAsync(baseUri, cancellationToken))
        {
            logger.LogInformation("Ses çözümleme servisi zaten hazır: {BaseUrl}", _options.BaseUrl);
            return;
        }

        var inferenceDirectory = ResolveInferenceDirectory(environment.ContentRootPath);
        var pythonPath = Path.Combine(inferenceDirectory, ".venv", "Scripts", "python.exe");
        if (!File.Exists(pythonPath))
        {
            throw new InvalidOperationException(
                $"Ses çözümleme servisi başlatılamadı. Python ortamı bulunamadı: {pythonPath}");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = pythonPath,
            WorkingDirectory = inferenceDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-m");
        startInfo.ArgumentList.Add("uvicorn");
        startInfo.ArgumentList.Add("app.main:app");
        startInfo.ArgumentList.Add("--host");
        startInfo.ArgumentList.Add(baseUri.Host);
        startInfo.ArgumentList.Add("--port");
        startInfo.ArgumentList.Add(baseUri.Port.ToString());

        // Model bu makinede önceden indirilmiştir. Offline kip, Transformers'ın
        // her açılışta uzak HEAD istekleriyle dakikalarca beklemesini önler.
        startInfo.Environment["YAZTEK_MODEL_LOCAL_ONLY"] = "true";
        startInfo.Environment["HF_HUB_OFFLINE"] = "1";
        startInfo.Environment["TRANSFORMERS_OFFLINE"] = "1";
        startInfo.Environment["PYTHONUNBUFFERED"] = "1";
        startInfo.Environment["YAZTEK_TEMP_DIR"] = Path.Combine(inferenceDirectory, ".runtime-temp");

        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, eventArgs) =>
        {
            if (!string.IsNullOrWhiteSpace(eventArgs.Data))
            {
                logger.LogInformation("ASR: {Message}", eventArgs.Data);
            }
        };
        _process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (!string.IsNullOrWhiteSpace(eventArgs.Data))
            {
                logger.LogInformation("ASR: {Message}", eventArgs.Data);
            }
        };

        if (!_process.Start())
        {
            throw new InvalidOperationException("Ses çözümleme servisi süreci başlatılamadı.");
        }
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        var deadline = DateTimeOffset.UtcNow.AddSeconds(_options.StartupTimeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_process.HasExited)
            {
                throw new InvalidOperationException(
                    $"Ses çözümleme servisi hazır olmadan kapandı (çıkış kodu: {_process.ExitCode}).");
            }
            if (await IsReadyAsync(baseUri, cancellationToken))
            {
                logger.LogInformation("Yerel ses çözümleme servisi hazır: {BaseUrl}", _options.BaseUrl);
                return;
            }
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        StopOwnedProcess();
        throw new TimeoutException(
            $"Ses çözümleme servisi {_options.StartupTimeoutSeconds} saniye içinde hazır olmadı.");
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        StopOwnedProcess();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        StopOwnedProcess();
        _process?.Dispose();
    }

    private static string ResolveInferenceDirectory(string contentRootPath)
    {
        var directory = new DirectoryInfo(contentRootPath);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "inference");
            if (File.Exists(Path.Combine(candidate, "app", "main.py")))
            {
                return candidate;
            }
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"YAZTEK inference klasörü '{contentRootPath}' üst dizinlerinde bulunamadı.");
    }

    private static async Task<bool> IsReadyAsync(Uri baseUri, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            using var response = await client.GetAsync(new Uri(baseUri, "health"), cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }
            var health = await response.Content.ReadFromJsonAsync<SpeechHealth>(cancellationToken);
            return health?.Ready is true;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    private void StopOwnedProcess()
    {
        if (_process is not { HasExited: false })
        {
            return;
        }

        try
        {
            _process.Kill(entireProcessTree: true);
            _process.WaitForExit(5_000);
        }
        catch (InvalidOperationException)
        {
            // Süreç sağlık kontrolü ile sonlandırma arasındaki anda kapanmış olabilir.
        }
    }

    private sealed record SpeechHealth(bool Ready);
}
