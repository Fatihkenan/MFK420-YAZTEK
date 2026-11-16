namespace YAZTEK.Domain;

public enum ComprehensionAttemptStatus
{
    Generating,
    Ready,
    Submitted,
    ReviewRequired,
    Failed
}

public enum ComprehensionQuestionType
{
    MultipleChoice,
    ShortAnswer
}

public enum ComprehensionEvaluationStatus
{
    Correct,
    Partial,
    Incorrect,
    ReviewRequired
}

public sealed class ComprehensionTestAttempt
{
    public Guid Id { get; set; }
    public required string StudentId { get; set; }
    public required string TextId { get; set; }
    public required string TextTitle { get; set; }
    public required string SourceText { get; set; }
    public required string SourceTextHash { get; set; }
    public required string Language { get; set; }
    public required string IdempotencyKey { get; set; }
    public required string ModelId { get; set; }
    public required string PromptVersion { get; set; }
    public ComprehensionAttemptStatus Status { get; set; }
    public decimal? TotalScore { get; set; }
    public decimal MaxScore { get; set; } = 7;
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? SubmittedAtUtc { get; set; }
    public List<ComprehensionQuestion> Questions { get; set; } = [];
}

public sealed class ComprehensionQuestion
{
    public Guid Id { get; set; }
    public Guid AttemptId { get; set; }
    public ComprehensionTestAttempt Attempt { get; set; } = null!;
    public int Order { get; set; }
    public ComprehensionQuestionType Type { get; set; }
    public required string Skill { get; set; }
    public required string Difficulty { get; set; }
    public required string Prompt { get; set; }
    public string? OptionsJson { get; set; }
    public int? CorrectOptionIndex { get; set; }
    public required string ReferenceAnswer { get; set; }
    public required string Explanation { get; set; }
    public required string Evidence { get; set; }
    public required string RubricJson { get; set; }
    public required string NormalizedPromptHash { get; set; }
    public ComprehensionAnswer? Answer { get; set; }
}

public sealed class ComprehensionAnswer
{
    public Guid Id { get; set; }
    public Guid QuestionId { get; set; }
    public ComprehensionQuestion Question { get; set; } = null!;
    public int? SelectedOptionIndex { get; set; }
    public string? TextAnswer { get; set; }
    public ComprehensionEvaluationStatus Status { get; set; }
    public decimal AwardedPoints { get; set; }
    public decimal Confidence { get; set; }
    public required string Feedback { get; set; }
    public DateTimeOffset SubmittedAtUtc { get; set; }
}
