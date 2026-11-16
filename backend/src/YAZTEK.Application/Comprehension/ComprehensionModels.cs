using YAZTEK.Domain;

namespace YAZTEK.Application.Comprehension;

public sealed record CreateComprehensionAttempt(
    string StudentId,
    string TextId,
    string TextTitle,
    string SourceText,
    string IdempotencyKey);

public sealed record GeneratedQuestion(
    string Type,
    string Skill,
    string Difficulty,
    string Prompt,
    IReadOnlyList<string> Options,
    int? CorrectOptionIndex,
    string Answer,
    string Explanation,
    string Evidence,
    IReadOnlyList<string> EvaluationCriteria);

public sealed record GenerationRequest(
    string Language,
    string TextTitle,
    string SourceText,
    IReadOnlyList<string> PreviousQuestions,
    IReadOnlyList<string> WeakSkills,
    IReadOnlyList<string> ValidationErrors,
    int MultipleChoiceCount = 4,
    int ShortAnswerCount = 3,
    IReadOnlyList<string>? UsedEvidence = null,
    IReadOnlyList<GeneratedQuestion>? AcceptedQuestions = null);

public sealed record QuestionSetReview(bool Passed, IReadOnlyList<string> Issues);

public sealed record ComprehensionAiHealth(
    bool IsReady,
    string Provider,
    string Model,
    string Status,
    string? Detail);

public sealed record OpenAnswerEvaluationRequest(
    Guid QuestionId,
    string Language,
    string SourceText,
    string Prompt,
    string StudentAnswer,
    string ReferenceAnswer,
    string Evidence,
    IReadOnlyList<string> Criteria);

public sealed record OpenAnswerEvaluation(
    Guid QuestionId,
    string Status,
    decimal Score,
    decimal Confidence,
    bool IsRelevant,
    bool IsSupportedByEvidence,
    bool ContradictsReference,
    string Feedback,
    string Evidence);

public sealed record StudentQuestion(
    Guid Id,
    int Order,
    string Type,
    string Skill,
    string Difficulty,
    string Prompt,
    IReadOnlyList<string> Options);

public sealed record StudentAttempt(
    Guid Id,
    string TextId,
    string TextTitle,
    string Language,
    string Status,
    string ModelId,
    decimal? TotalScore,
    decimal MaxScore,
    IReadOnlyList<StudentQuestion> Questions,
    string? Error);

public sealed record SubmittedAnswerInput(Guid QuestionId, int? SelectedOptionIndex, string? TextAnswer);
public sealed record SubmitComprehensionAttempt(string StudentId, IReadOnlyList<SubmittedAnswerInput> Answers);

public sealed record AnswerFeedback(
    Guid QuestionId,
    string Status,
    decimal Score,
    decimal Confidence,
    string Feedback,
    string Explanation,
    string? Evidence,
    bool EvidenceVerified,
    int? CorrectOptionIndex,
    string ReferenceAnswer);

public sealed record SkillResult(string Skill, decimal Score, decimal Maximum);

public sealed record ComprehensionResult(
    Guid AttemptId,
    decimal? TotalScore,
    decimal MaxScore,
    bool IsFinal,
    int ReviewRequiredCount,
    IReadOnlyList<AnswerFeedback> Answers,
    IReadOnlyList<SkillResult> Skills);

public sealed record AttemptContext(
    ComprehensionTestAttempt Attempt,
    bool Created,
    IReadOnlyList<string> PreviousQuestions,
    IReadOnlyList<string> WeakSkills);

public sealed class ComprehensionServiceException(
    string code,
    string message,
    int statusCode,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}
