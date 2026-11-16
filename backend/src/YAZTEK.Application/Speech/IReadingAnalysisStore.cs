using YAZTEK.Domain;

namespace YAZTEK.Application.Speech;

public interface IReadingAnalysisStore
{
    Task SaveAsync(ReadingAnalysisReport report, CancellationToken cancellationToken);
    Task<int> DeleteExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken);
}
