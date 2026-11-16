using YAZTEK.Domain;

namespace YAZTEK.Application.Comprehension;

public interface IComprehensionAiClient
{
    string ModelId { get; }
    Task<ComprehensionAiHealth> GetHealthAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<GeneratedQuestion>> GenerateQuestionsAsync(GenerationRequest request, CancellationToken cancellationToken);
    Task<QuestionSetReview> ReviewQuestionSetAsync(GenerationRequest request, IReadOnlyList<GeneratedQuestion> questions, CancellationToken cancellationToken);
    Task<IReadOnlyList<OpenAnswerEvaluation>> EvaluateAnswersAsync(
        IReadOnlyList<OpenAnswerEvaluationRequest> requests,
        CancellationToken cancellationToken);
}

public interface IComprehensionAttemptStore
{
    Task<AttemptContext> GetOrCreateAsync(ComprehensionTestAttempt attempt, CancellationToken cancellationToken);
    Task SaveGeneratedQuestionsAsync(Guid attemptId, IReadOnlyList<ComprehensionQuestion> questions, CancellationToken cancellationToken);
    Task MarkFailedAsync(Guid attemptId, string code, string message, CancellationToken cancellationToken);
    Task<ComprehensionTestAttempt?> GetForStudentAsync(Guid attemptId, string studentId, CancellationToken cancellationToken);
    Task SaveAnswersAsync(Guid attemptId, IReadOnlyList<ComprehensionAnswer> answers, decimal? totalScore, CancellationToken cancellationToken);
}

public interface IComprehensionService
{
    Task<StudentAttempt> CreateAttemptAsync(CreateComprehensionAttempt request, CancellationToken cancellationToken);
    Task<ComprehensionResult> SubmitAsync(Guid attemptId, SubmitComprehensionAttempt request, CancellationToken cancellationToken);
}
