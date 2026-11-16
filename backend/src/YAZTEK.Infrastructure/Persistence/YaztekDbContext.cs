using Microsoft.EntityFrameworkCore;
using YAZTEK.Domain;

namespace YAZTEK.Infrastructure.Persistence;

public sealed class YaztekDbContext(DbContextOptions<YaztekDbContext> options) : DbContext(options)
{
    public DbSet<ReadingAnalysisReport> ReadingAnalysisReports => Set<ReadingAnalysisReport>();
    public DbSet<ComprehensionTestAttempt> ComprehensionTestAttempts => Set<ComprehensionTestAttempt>();
    public DbSet<ComprehensionQuestion> ComprehensionQuestions => Set<ComprehensionQuestion>();
    public DbSet<ComprehensionAnswer> ComprehensionAnswers => Set<ComprehensionAnswer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var report = modelBuilder.Entity<ReadingAnalysisReport>();
        report.ToTable("reading_analysis_reports");
        report.HasKey(item => item.Id);
        report.Property(item => item.StudentId).HasColumnName("student_id").HasMaxLength(160).IsRequired();
        report.Property(item => item.TextId).HasColumnName("text_id").HasMaxLength(160).IsRequired();
        report.Property(item => item.ReferenceTextHash).HasColumnName("reference_text_hash").HasMaxLength(64).IsRequired();
        report.Property(item => item.Transcript).HasColumnName("transcript").HasMaxLength(20_000);
        report.Property(item => item.ModelId).HasColumnName("model_id").HasMaxLength(240).IsRequired();
        report.Property(item => item.AnalysisJson).HasColumnName("analysis_json").HasColumnType("jsonb").IsRequired();
        report.Property(item => item.ConsentGranted).HasColumnName("consent_granted").IsRequired();
        report.Property(item => item.AudioRetained).HasColumnName("audio_retained").IsRequired();
        report.Property(item => item.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        report.Property(item => item.ExpiresAtUtc).HasColumnName("expires_at_utc").IsRequired();
        report.HasIndex(item => new { item.StudentId, item.CreatedAtUtc });
        report.HasIndex(item => item.ExpiresAtUtc);

        var attempt = modelBuilder.Entity<ComprehensionTestAttempt>();
        attempt.ToTable("comprehension_test_attempts");
        attempt.HasKey(item => item.Id);
        attempt.Property(item => item.StudentId).HasColumnName("student_id").HasMaxLength(160).IsRequired();
        attempt.Property(item => item.TextId).HasColumnName("text_id").HasMaxLength(160).IsRequired();
        attempt.Property(item => item.TextTitle).HasColumnName("text_title").HasMaxLength(300).IsRequired();
        attempt.Property(item => item.SourceText).HasColumnName("source_text").HasMaxLength(20_000).IsRequired();
        attempt.Property(item => item.SourceTextHash).HasColumnName("source_text_hash").HasMaxLength(64).IsRequired();
        attempt.Property(item => item.Language).HasColumnName("language").HasMaxLength(8).IsRequired();
        attempt.Property(item => item.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(64).IsRequired();
        attempt.Property(item => item.ModelId).HasColumnName("model_id").HasMaxLength(120).IsRequired();
        attempt.Property(item => item.PromptVersion).HasColumnName("prompt_version").HasMaxLength(80).IsRequired();
        attempt.Property(item => item.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(24).IsRequired();
        attempt.Property(item => item.TotalScore).HasColumnName("total_score").HasPrecision(4, 2);
        attempt.Property(item => item.MaxScore).HasColumnName("max_score").HasPrecision(4, 2).IsRequired();
        attempt.Property(item => item.FailureCode).HasColumnName("failure_code").HasMaxLength(80);
        attempt.Property(item => item.FailureMessage).HasColumnName("failure_message").HasMaxLength(500);
        attempt.Property(item => item.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        attempt.Property(item => item.SubmittedAtUtc).HasColumnName("submitted_at_utc");
        attempt.HasIndex(item => new { item.StudentId, item.TextId, item.IdempotencyKey }).IsUnique();
        attempt.HasIndex(item => new { item.StudentId, item.TextId, item.SourceTextHash, item.CreatedAtUtc });

        var question = modelBuilder.Entity<ComprehensionQuestion>();
        question.ToTable("comprehension_questions");
        question.HasKey(item => item.Id);
        question.Property(item => item.AttemptId).HasColumnName("attempt_id").IsRequired();
        question.Property(item => item.Order).HasColumnName("question_order").IsRequired();
        question.Property(item => item.Type).HasColumnName("question_type").HasConversion<string>().HasMaxLength(24).IsRequired();
        question.Property(item => item.Skill).HasColumnName("skill").HasMaxLength(80).IsRequired();
        question.Property(item => item.Difficulty).HasColumnName("difficulty").HasMaxLength(24).IsRequired();
        question.Property(item => item.Prompt).HasColumnName("prompt").HasMaxLength(2000).IsRequired();
        question.Property(item => item.OptionsJson).HasColumnName("options_json").HasColumnType("jsonb");
        question.Property(item => item.CorrectOptionIndex).HasColumnName("correct_option_index");
        question.Property(item => item.ReferenceAnswer).HasColumnName("reference_answer").HasMaxLength(4000).IsRequired();
        question.Property(item => item.Explanation).HasColumnName("explanation").HasMaxLength(4000).IsRequired();
        question.Property(item => item.Evidence).HasColumnName("evidence").HasMaxLength(4000).IsRequired();
        question.Property(item => item.RubricJson).HasColumnName("rubric_json").HasColumnType("jsonb").IsRequired();
        question.Property(item => item.NormalizedPromptHash).HasColumnName("normalized_prompt_hash").HasMaxLength(64).IsRequired();
        question.HasOne(item => item.Attempt).WithMany(item => item.Questions).HasForeignKey(item => item.AttemptId).OnDelete(DeleteBehavior.Cascade);
        question.HasIndex(item => new { item.AttemptId, item.Order }).IsUnique();
        question.HasIndex(item => item.NormalizedPromptHash);

        var answer = modelBuilder.Entity<ComprehensionAnswer>();
        answer.ToTable("comprehension_answers");
        answer.HasKey(item => item.Id);
        answer.Property(item => item.QuestionId).HasColumnName("question_id").IsRequired();
        answer.Property(item => item.SelectedOptionIndex).HasColumnName("selected_option_index");
        answer.Property(item => item.TextAnswer).HasColumnName("text_answer").HasMaxLength(4000);
        answer.Property(item => item.Status).HasColumnName("evaluation_status").HasConversion<string>().HasMaxLength(32).IsRequired();
        answer.Property(item => item.AwardedPoints).HasColumnName("awarded_points").HasPrecision(4, 2).IsRequired();
        answer.Property(item => item.Confidence).HasColumnName("confidence").HasPrecision(5, 4).IsRequired();
        answer.Property(item => item.Feedback).HasColumnName("feedback").HasMaxLength(4000).IsRequired();
        answer.Property(item => item.SubmittedAtUtc).HasColumnName("submitted_at_utc").IsRequired();
        answer.HasOne(item => item.Question).WithOne(item => item.Answer).HasForeignKey<ComprehensionAnswer>(item => item.QuestionId).OnDelete(DeleteBehavior.Cascade);
        answer.HasIndex(item => item.QuestionId).IsUnique();
    }
}
