using Microsoft.EntityFrameworkCore;
using YAZTEK.Application.Comprehension;
using YAZTEK.Domain;
using YAZTEK.Infrastructure.Persistence;

namespace YAZTEK.Infrastructure.Comprehension;

public sealed class ComprehensionAttemptStore(YaztekDbContext dbContext) : IComprehensionAttemptStore
{
    public async Task<AttemptContext> GetOrCreateAsync(ComprehensionTestAttempt attempt, CancellationToken cancellationToken)
    {
        var existing = await FindByIdempotencyAsync(attempt.StudentId, attempt.TextId, attempt.IdempotencyKey, cancellationToken);
        if (existing is not null) return new AttemptContext(existing, false, [], []);

        dbContext.ComprehensionTestAttempts.Add(attempt);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(attempt).State = EntityState.Detached;
            existing = await FindByIdempotencyAsync(attempt.StudentId, attempt.TextId, attempt.IdempotencyKey, cancellationToken);
            if (existing is null) throw;
            return new AttemptContext(existing, false, [], []);
        }

        var previousQuestions = await dbContext.ComprehensionQuestions
            .Where(question => question.Attempt.StudentId == attempt.StudentId
                && question.Attempt.TextId == attempt.TextId
                && question.Attempt.SourceTextHash == attempt.SourceTextHash
                && question.AttemptId != attempt.Id)
            .OrderBy(question => question.Attempt.CreatedAtUtc)
            .ThenBy(question => question.Order)
            .Select(question => question.Prompt)
            .ToListAsync(cancellationToken);

        var weakSkills = await dbContext.ComprehensionQuestions
            .Where(question => question.Attempt.StudentId == attempt.StudentId
                && question.Attempt.TextId == attempt.TextId
                && question.Attempt.SourceTextHash == attempt.SourceTextHash
                && question.Answer != null
                && question.Answer.AwardedPoints < 0.75m)
            .GroupBy(question => question.Skill)
            .OrderByDescending(group => group.Count())
            .Select(group => group.Key)
            .Take(3)
            .ToListAsync(cancellationToken);

        return new AttemptContext(attempt, true, previousQuestions, weakSkills);
    }

    public async Task SaveGeneratedQuestionsAsync(Guid attemptId, IReadOnlyList<ComprehensionQuestion> questions, CancellationToken cancellationToken)
    {
        var attempt = await dbContext.ComprehensionTestAttempts.SingleAsync(item => item.Id == attemptId, cancellationToken);
        dbContext.ComprehensionQuestions.AddRange(questions);
        attempt.Status = ComprehensionAttemptStatus.Ready;
        attempt.FailureCode = null;
        attempt.FailureMessage = null;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkFailedAsync(Guid attemptId, string code, string message, CancellationToken cancellationToken)
    {
        var attempt = await dbContext.ComprehensionTestAttempts.SingleOrDefaultAsync(item => item.Id == attemptId, cancellationToken);
        if (attempt is null) return;
        attempt.Status = ComprehensionAttemptStatus.Failed;
        attempt.FailureCode = code[..Math.Min(code.Length, 80)];
        attempt.FailureMessage = message[..Math.Min(message.Length, 500)];
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<ComprehensionTestAttempt?> GetForStudentAsync(Guid attemptId, string studentId, CancellationToken cancellationToken) =>
        dbContext.ComprehensionTestAttempts
            .Include(attempt => attempt.Questions.OrderBy(question => question.Order))
            .ThenInclude(question => question.Answer)
            .SingleOrDefaultAsync(attempt => attempt.Id == attemptId && attempt.StudentId == studentId, cancellationToken);

    public async Task SaveAnswersAsync(Guid attemptId, IReadOnlyList<ComprehensionAnswer> answers, decimal? totalScore, CancellationToken cancellationToken)
    {
        var attempt = await dbContext.ComprehensionTestAttempts.SingleAsync(item => item.Id == attemptId, cancellationToken);
        dbContext.ComprehensionAnswers.AddRange(answers);
        attempt.TotalScore = totalScore;
        attempt.Status = totalScore.HasValue
            ? ComprehensionAttemptStatus.Submitted
            : ComprehensionAttemptStatus.ReviewRequired;
        attempt.SubmittedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private Task<ComprehensionTestAttempt?> FindByIdempotencyAsync(string studentId, string textId, string idempotencyKey, CancellationToken cancellationToken) =>
        dbContext.ComprehensionTestAttempts
            .Include(attempt => attempt.Questions.OrderBy(question => question.Order))
            .ThenInclude(question => question.Answer)
            .SingleOrDefaultAsync(attempt => attempt.StudentId == studentId
                && attempt.TextId == textId
                && attempt.IdempotencyKey == idempotencyKey, cancellationToken);
}
