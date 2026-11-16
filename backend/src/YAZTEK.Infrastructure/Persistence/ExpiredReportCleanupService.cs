using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YAZTEK.Application.Speech;
using YAZTEK.Infrastructure.Configuration;

namespace YAZTEK.Infrastructure.Persistence;

public sealed class ExpiredReportCleanupService(
    IServiceScopeFactory scopeFactory,
    IOptions<AnalysisOptions> options,
    ILogger<ExpiredReportCleanupService> logger) : BackgroundService
{
    private readonly AnalysisOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.PersistReports)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var store = scope.ServiceProvider.GetRequiredService<IReadingAnalysisStore>();
                var deleted = await store.DeleteExpiredAsync(DateTimeOffset.UtcNow, stoppingToken);
                if (deleted > 0)
                {
                    logger.LogInformation("Deleted {Count} expired reading analysis reports.", deleted);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Expired reading analysis cleanup failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
