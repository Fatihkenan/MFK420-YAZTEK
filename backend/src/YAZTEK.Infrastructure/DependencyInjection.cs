using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using YAZTEK.Application.Comprehension;
using YAZTEK.Application.Speech;
using YAZTEK.Infrastructure.Comprehension;
using YAZTEK.Infrastructure.Configuration;
using YAZTEK.Infrastructure.Persistence;
using YAZTEK.Infrastructure.Speech;

namespace YAZTEK.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddYaztekInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<PostgreSqlOptions>()
            .Bind(configuration.GetSection(PostgreSqlOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.ConnectionStringName),
                "PostgreSQL connection string name is required.")
            .ValidateOnStart();

        services
            .AddOptions<AnalysisOptions>()
            .Bind(configuration.GetSection(AnalysisOptions.SectionName))
            .Validate(options => options.MaxUploadBytes > 0, "Analysis upload limit must be positive.")
            .Validate(options => options.RetentionDays > 0, "Analysis retention must be positive.")
            .ValidateOnStart();

        services
            .AddOptions<SpeechInferenceOptions>()
            .Bind(configuration.GetSection(SpeechInferenceOptions.SectionName))
            .Validate(options => Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _), "Speech inference BaseUrl must be absolute.")
            .Validate(options => options.TimeoutSeconds > 0, "Speech inference timeout must be positive.")
            .Validate(options => options.StartupTimeoutSeconds > 0, "Speech inference startup timeout must be positive.")
            .ValidateOnStart();

        services
            .AddOptions<ComprehensionAiOptions>()
            .Bind(configuration.GetSection(ComprehensionAiOptions.SectionName))
            .Validate(options => options.Provider.Equals("Ollama", StringComparison.OrdinalIgnoreCase), "Comprehension AI provider must be Ollama.")
            .Validate(options => Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _), "Comprehension AI BaseUrl must be absolute.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.Model), "Comprehension AI model is required.")
            .Validate(options => options.TimeoutSeconds > 0 && options.NumContext >= 4_096 && options.NumPredict >= 512,
                "Comprehension AI limits are invalid.")
            .Validate(options => options.QuestionBatchSize is >= 1 and <= 4 && options.MaxOutputRetries is >= 0 and <= 2,
                "Comprehension AI batching limits are invalid.")
            .Validate(options => options.QualityReviewBatchSize is >= 1 and <= 7
                && options.QualityReviewNumPredict >= 256 && options.EvaluationNumPredict >= 512,
                "Comprehension AI review/evaluation limits are invalid.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.KeepAlive), "Comprehension AI keep-alive is required.")
            .ValidateOnStart();

        var connectionStringName = configuration[$"{PostgreSqlOptions.SectionName}:ConnectionStringName"] ?? "PostgreSql";
        var connectionString = configuration.GetConnectionString(connectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{connectionStringName}' is missing.");
        services.AddDbContext<YaztekDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IReadingAnalysisStore, ReadingAnalysisStore>();
        services.AddScoped<IComprehensionAttemptStore, ComprehensionAttemptStore>();
        services.AddScoped<IComprehensionService, ComprehensionService>();
        services.AddHostedService<ExpiredReportCleanupService>();
        services.AddHostedService<LocalSpeechInferenceHostedService>();
        services.AddHttpClient<ISpeechInferenceClient, SpeechInferenceClient>((provider, client) =>
        {
            var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<SpeechInferenceOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });
        services.AddHttpClient<IComprehensionAiClient, OllamaComprehensionClient>((provider, client) =>
        {
            var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ComprehensionAiOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });

        return services;
    }
}
