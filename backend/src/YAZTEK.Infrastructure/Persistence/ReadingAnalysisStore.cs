using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using YAZTEK.Application.Speech;
using YAZTEK.Domain;
using YAZTEK.Infrastructure.Configuration;

namespace YAZTEK.Infrastructure.Persistence;

public sealed class ReadingAnalysisStore(
    YaztekDbContext dbContext,
    IOptions<AnalysisOptions> options) : IReadingAnalysisStore
{
    private readonly AnalysisOptions _options = options.Value;

    public async Task SaveAsync(ReadingAnalysisReport report, CancellationToken cancellationToken)
    {
        if (!_options.PersistReports)
        {
            return;
        }

        dbContext.ReadingAnalysisReports.Add(report);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<int> DeleteExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!_options.PersistReports)
        {
            return Task.FromResult(0);
        }

        return dbContext.ReadingAnalysisReports
            .Where(report => report.ExpiresAtUtc < now)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
