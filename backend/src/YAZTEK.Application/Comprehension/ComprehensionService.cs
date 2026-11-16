using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using YAZTEK.Domain;

namespace YAZTEK.Application.Comprehension;

public sealed class ComprehensionService(
    IComprehensionAttemptStore store,
    IComprehensionAiClient aiClient) : IComprehensionService
{
    public const string PromptVersion = "comprehension-v1";

    public async Task<StudentAttempt> CreateAttemptAsync(CreateComprehensionAttempt request, CancellationToken cancellationToken)
    {
        ValidateCreateRequest(request);
        var attempt = new ComprehensionTestAttempt
        {
            Id = Guid.NewGuid(),
            StudentId = request.StudentId.Trim(),
            TextId = request.TextId.Trim(),
            TextTitle = request.TextTitle.Trim(),
            SourceText = request.SourceText.Trim(),
            SourceTextHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.SourceText.Trim()))),
            Language = QuestionSetValidator.DetectLanguage(request.SourceText),
            IdempotencyKey = request.IdempotencyKey.Trim(),
            ModelId = aiClient.ModelId,
            PromptVersion = PromptVersion,
            Status = ComprehensionAttemptStatus.Generating,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        var context = await store.GetOrCreateAsync(attempt, cancellationToken);
        if (!context.Created || context.Attempt.Status != ComprehensionAttemptStatus.Generating)
        {
            return ToStudentAttempt(context.Attempt);
        }

        IReadOnlyList<string> validationErrors = [];
        try
        {
            var promptHistory = BuildAvoidanceList(context.PreviousQuestions, []);
            List<GeneratedQuestion>? candidate = null;
            IReadOnlyList<int> repairIndexes = [];
            const int maxGenerationCalls = 3;
            for (var generation = 0; generation < maxGenerationCalls; generation++)
            {
                var replacementMultipleChoice = candidate is null
                    ? 4
                    : repairIndexes.Count(index => IsMultipleChoiceType(candidate[index].Type));
                var replacementShortAnswer = candidate is null
                    ? 3
                    : repairIndexes.Count - replacementMultipleChoice;
                var requestedMultipleChoice = candidate is null
                    ? replacementMultipleChoice
                    : replacementMultipleChoice + Math.Min(replacementMultipleChoice, 1);
                var requestedShortAnswer = candidate is null
                    ? replacementShortAnswer
                    : replacementShortAnswer + Math.Min(replacementShortAnswer, 1);
                var acceptedQuestions = candidate is null
                    ? []
                    : candidate.Where((_, index) => !repairIndexes.Contains(index)).ToList();
                var acceptedEvidence = acceptedQuestions
                    .Select(question => question.Evidence)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                var generatedRaw = await aiClient.GenerateQuestionsAsync(new GenerationRequest(
                    attempt.Language,
                    attempt.TextTitle,
                    attempt.SourceText,
                    promptHistory,
                    context.WeakSkills,
                    validationErrors,
                    requestedMultipleChoice,
                    requestedShortAnswer,
                    acceptedEvidence,
                    acceptedQuestions), cancellationToken);
                var grounded = generatedRaw
                    .Select(question => QuestionSetValidator.GroundEvidence(question, attempt.SourceText))
                    .ToList();
                if (candidate is null)
                {
                    candidate = grounded;
                }
                else
                {
                    ApplyBestReplacements(candidate, repairIndexes, grounded,
                        attempt.SourceText, context.PreviousQuestions);
                }

                validationErrors = QuestionSetValidator.Validate(candidate, attempt.SourceText, context.PreviousQuestions);
                if (validationErrors.Count == 0)
                {
                    var review = await aiClient.ReviewQuestionSetAsync(new GenerationRequest(
                        attempt.Language, attempt.TextTitle, attempt.SourceText, promptHistory,
                        context.WeakSkills, []), candidate, cancellationToken);
                    if (review.Passed)
                    {
                        var questions = candidate.Select((question, index) => QuestionSetValidator.ToEntity(question, attempt.Id, index + 1)).ToList();
                        await store.SaveGeneratedQuestionsAsync(attempt.Id, questions, cancellationToken);
                        attempt.Questions = questions;
                        attempt.Status = ComprehensionAttemptStatus.Ready;
                        return ToStudentAttempt(attempt);
                    }
                    validationErrors = review.Issues.Count > 0 ? review.Issues : ["Bağımsız kalite incelemesi soru setini reddetti."];
                }

                if (generation == maxGenerationCalls - 1) break;
                repairIndexes = QuestionSetValidator.RepairableQuestionIndexes(validationErrors);
                var hasSetLevelError = validationErrors.Any(error => !error.StartsWith("Soru ", StringComparison.Ordinal));
                if (repairIndexes.Count == 0 || repairIndexes.Count > 4 || hasSetLevelError)
                {
                    candidate = null;
                    repairIndexes = [];
                }
                else
                {
                    // Include the rejected wording itself even when it came from history older than the
                    // compact prompt window. Accepted questions are also forbidden so replacements cannot
                    // collide with the set that will be retained.
                    promptHistory = BuildAvoidanceList(context.PreviousQuestions, candidate.Select(question => question.Prompt));
                }
            }

            throw new ComprehensionServiceException(
                "question_quality_failed",
                $"Metne dayalı yedi kaliteli soru üretilemedi: {string.Join(" ", validationErrors.Take(3))}",
                422);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var serviceException = exception as ComprehensionServiceException;
            var code = serviceException?.Code ?? "question_generation_failed";
            var message = serviceException?.Message ?? "Soru üretim servisine şu anda ulaşılamıyor.";
            await store.MarkFailedAsync(attempt.Id, code, message, CancellationToken.None);
            if (serviceException is not null) throw;
            throw new ComprehensionServiceException(code, message, 503, exception);
        }
    }

    private static List<string> BuildAvoidanceList(
        IReadOnlyList<string> previousQuestions,
        IEnumerable<string> currentQuestions)
    {
        const int historyLimit = 35;
        var recentHistory = previousQuestions
            .Where(question => !string.IsNullOrWhiteSpace(question))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .TakeLast(historyLimit);
        return recentHistory
            .Concat(currentQuestions.Where(question => !string.IsNullOrWhiteSpace(question)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void ApplyBestReplacements(
        List<GeneratedQuestion> candidate,
        IReadOnlyList<int> repairIndexes,
        IReadOnlyList<GeneratedQuestion> replacements,
        string sourceText,
        IReadOnlyList<string> previousQuestions)
    {
        var available = replacements.ToList();
        var repairSet = repairIndexes.ToHashSet();
        var forbidden = previousQuestions
            .Concat(candidate.Where((_, index) => !repairSet.Contains(index)).Select(question => question.Prompt))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var index in repairIndexes)
        {
            var expectedMultipleChoice = IsMultipleChoiceType(candidate[index].Type);
            var selectedIndex = available.FindIndex(question =>
                IsMultipleChoiceType(question.Type) == expectedMultipleChoice
                && QuestionSetValidator.ValidateReplacement(question, sourceText, forbidden).Count == 0);
            if (selectedIndex < 0) continue;
            var selected = available[selectedIndex];
            available.RemoveAt(selectedIndex);
            candidate[index] = selected;
            forbidden.Add(selected.Prompt);
        }
    }

    private static bool IsMultipleChoiceType(string type) =>
        QuestionSetValidator.Normalize(type) is "multiple_choice" or "multiplechoice" or "çoktan seçmeli";

    public async Task<ComprehensionResult> SubmitAsync(Guid attemptId, SubmitComprehensionAttempt request, CancellationToken cancellationToken)
    {
        var attempt = await store.GetForStudentAsync(attemptId, request.StudentId.Trim(), cancellationToken)
            ?? throw new ComprehensionServiceException("attempt_not_found", "Test denemesi bulunamadı.", 404);
        if (attempt.Status is ComprehensionAttemptStatus.Submitted or ComprehensionAttemptStatus.ReviewRequired)
        {
            return BuildExistingResult(attempt);
        }
        if (attempt.Status != ComprehensionAttemptStatus.Ready)
        {
            throw new ComprehensionServiceException("attempt_not_ready", "Test henüz yanıtlanmaya hazır değil.", 409);
        }

        var supplied = request.Answers.GroupBy(answer => answer.QuestionId).ToDictionary(group => group.Key, group => group.Single());
        if (supplied.Count != 7 || attempt.Questions.Any(question => !supplied.ContainsKey(question.Id)))
        {
            throw new ComprehensionServiceException("all_answers_required", "Yedi sorunun tamamını yanıtlayın.", 400);
        }

        var now = DateTimeOffset.UtcNow;
        var answers = new List<ComprehensionAnswer>();
        var openRequests = new List<OpenAnswerEvaluationRequest>();
        foreach (var question in attempt.Questions.OrderBy(question => question.Order))
        {
            var input = supplied[question.Id];
            if (question.Type == ComprehensionQuestionType.MultipleChoice)
            {
                if (input.SelectedOptionIndex is < 0 or > 3)
                    throw new ComprehensionServiceException("invalid_choice", "Geçersiz bir seçenek gönderildi.", 400);
                var correct = input.SelectedOptionIndex == question.CorrectOptionIndex;
                answers.Add(new ComprehensionAnswer
                {
                    Id = Guid.NewGuid(), QuestionId = question.Id, SelectedOptionIndex = input.SelectedOptionIndex,
                    Status = correct ? ComprehensionEvaluationStatus.Correct : ComprehensionEvaluationStatus.Incorrect,
                    AwardedPoints = correct ? 1 : 0, Confidence = 1,
                    Feedback = correct
                        ? Localized(attempt.Language, "Doğru! Seçtiğin cevap metinle uyuşuyor.", "Correct! Your answer matches the text.")
                        : Localized(attempt.Language, "Bu cevap doğru değil. Doğru seçeneği ve metindeki kanıtı incele.", "This answer is not correct. Review the correct option and the evidence from the text."),
                    SubmittedAtUtc = now
                });
                continue;
            }

            var textAnswer = input.TextAnswer?.Trim();
            var invalidReason = InvalidOpenAnswerReason(textAnswer, question.Prompt, attempt.Language);
            if (invalidReason is not null)
            {
                answers.Add(new ComprehensionAnswer
                {
                    Id = Guid.NewGuid(), QuestionId = question.Id, TextAnswer = textAnswer,
                    Status = ComprehensionEvaluationStatus.Incorrect, AwardedPoints = 0, Confidence = 1,
                    Feedback = invalidReason, SubmittedAtUtc = now
                });
                continue;
            }
            openRequests.Add(new OpenAnswerEvaluationRequest(
                question.Id, attempt.Language, attempt.SourceText, question.Prompt, textAnswer!,
                question.ReferenceAnswer, question.Evidence,
                JsonSerializer.Deserialize<List<string>>(question.RubricJson) ?? []));
        }

        IReadOnlyList<OpenAnswerEvaluation> evaluations = [];
        if (openRequests.Count > 0)
        {
            try
            {
                evaluations = await aiClient.EvaluateAnswersAsync(openRequests, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                evaluations = [];
            }
        }
        var evaluationByQuestion = evaluations
            .GroupBy(item => item.QuestionId)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single());

        foreach (var evaluationRequest in openRequests)
        {
            var input = supplied[evaluationRequest.QuestionId];
            if (!evaluationByQuestion.TryGetValue(evaluationRequest.QuestionId, out var evaluation)
                || !IsConsistentEvaluation(evaluationRequest, evaluation))
            {
                answers.Add(new ComprehensionAnswer
                {
                    Id = Guid.NewGuid(), QuestionId = evaluationRequest.QuestionId, TextAnswer = input.TextAnswer?.Trim(),
                    Status = ComprehensionEvaluationStatus.ReviewRequired, AwardedPoints = 0, Confidence = 0,
                    Feedback = Localized(attempt.Language,
                        "Bu cevap güvenilir biçimde değerlendirilemedi. Mentörün incelemesi gerekiyor.",
                        "This answer could not be graded reliably. A mentor needs to review it."),
                    SubmittedAtUtc = now
                });
                continue;
            }

            var status = ParseStatus(evaluation.Status);
            var confidence = Math.Clamp(evaluation.Confidence, 0, 1);
            if (confidence < 0.65m) status = ComprehensionEvaluationStatus.ReviewRequired;
            var score = status switch
            {
                ComprehensionEvaluationStatus.Correct => 1m,
                ComprehensionEvaluationStatus.Partial => 0.5m,
                _ => 0m
            };
            answers.Add(new ComprehensionAnswer
            {
                Id = Guid.NewGuid(), QuestionId = evaluation.QuestionId, TextAnswer = input.TextAnswer?.Trim(),
                Status = status, AwardedPoints = score, Confidence = confidence,
                Feedback = EvaluationFeedback(attempt.Language, status, evaluation), SubmittedAtUtc = now
            });
        }

        var hasReviewRequired = answers.Any(answer => answer.Status == ComprehensionEvaluationStatus.ReviewRequired);
        decimal? total = hasReviewRequired ? null : answers.Sum(answer => answer.AwardedPoints);
        await store.SaveAnswersAsync(attempt.Id, answers, total, cancellationToken);
        foreach (var question in attempt.Questions) question.Answer = answers.Single(answer => answer.QuestionId == question.Id);
        attempt.Status = hasReviewRequired ? ComprehensionAttemptStatus.ReviewRequired : ComprehensionAttemptStatus.Submitted;
        attempt.TotalScore = total;
        attempt.SubmittedAtUtc = now;
        return BuildExistingResult(attempt);
    }

    private static void ValidateCreateRequest(CreateComprehensionAttempt request)
    {
        if (string.IsNullOrWhiteSpace(request.StudentId) || request.StudentId.Length > 160) throw new ComprehensionServiceException("invalid_student", "Öğrenci kimliği geçersiz.", 400);
        if (string.IsNullOrWhiteSpace(request.TextId) || request.TextId.Length > 160) throw new ComprehensionServiceException("invalid_text", "Metin kimliği geçersiz.", 400);
        if (string.IsNullOrWhiteSpace(request.TextTitle) || request.TextTitle.Length > 300) throw new ComprehensionServiceException("invalid_title", "Metin başlığı geçersiz.", 400);
        if (string.IsNullOrWhiteSpace(request.SourceText) || request.SourceText.Trim().Length is < 80 or > 20_000) throw new ComprehensionServiceException("text_scope_insufficient", "Yedi kaliteli soru için metin 80 ile 20.000 karakter arasında olmalıdır.", 422);
        if (!Guid.TryParse(request.IdempotencyKey, out _)) throw new ComprehensionServiceException("invalid_idempotency_key", "Geçerli bir idempotency anahtarı gereklidir.", 400);
    }

    private static ComprehensionEvaluationStatus ParseStatus(string status) => QuestionSetValidator.Normalize(status) switch
    {
        "correct" => ComprehensionEvaluationStatus.Correct,
        "partial" => ComprehensionEvaluationStatus.Partial,
        "incorrect" => ComprehensionEvaluationStatus.Incorrect,
        _ => ComprehensionEvaluationStatus.ReviewRequired
    };

    private static bool IsConsistentEvaluation(OpenAnswerEvaluationRequest request, OpenAnswerEvaluation evaluation)
    {
        if (evaluation.QuestionId != request.QuestionId
            || !string.Equals(evaluation.Evidence.Trim(), request.Evidence.Trim(), StringComparison.Ordinal)
            || !QuestionSetValidator.IsVerifiedEvidence(request.SourceText, request.Prompt, request.ReferenceAnswer, evaluation.Evidence)) return false;
        var status = QuestionSetValidator.Normalize(evaluation.Status);
        var groundedAnswer = QuestionSetValidator.HasGroundedAnswerContent(
            request.StudentAnswer,
            request.ReferenceAnswer,
            request.Evidence);
        return status switch
        {
            "correct" => evaluation.Score == 1 && evaluation.Confidence >= 0.65m
                && groundedAnswer && evaluation.IsRelevant && evaluation.IsSupportedByEvidence && !evaluation.ContradictsReference,
            "partial" => evaluation.Score == 0.5m && evaluation.Confidence >= 0.65m
                && groundedAnswer && evaluation.IsRelevant && !evaluation.ContradictsReference,
            "incorrect" => evaluation.Score == 0 && evaluation.Confidence >= 0.65m
                && (!evaluation.IsRelevant || !evaluation.IsSupportedByEvidence || evaluation.ContradictsReference),
            "review_required" => evaluation.Score == 0,
            _ => false
        };
    }

    private static string? InvalidOpenAnswerReason(string? answer, string prompt, string language)
    {
        if (string.IsNullOrWhiteSpace(answer))
            return Localized(language, "Bu soru boş bırakılmış. Bu nedenle puan verilmedi.", "This question was left blank, so no point was awarded.");
        var normalized = QuestionSetValidator.Normalize(answer);
        var compact = new string(normalized.Where(char.IsLetterOrDigit).ToArray());
        var knownNonAnswers = new HashSet<string>(StringComparer.Ordinal)
        {
            "bilmiyorum", "bilmiyom", "fikrimyok", "bilmiyorumki", "idk", "idontknow", "dontknow", "noidea",
            "abc", "abcd", "asdf", "qwe", "qwerty", "zxc", "test", "deneme", "cevap", "yanıt", "yanit", "evet", "hayır", "hayir", "tamam"
        };
        if (knownNonAnswers.Contains(compact))
            return Localized(language, "“Bilmiyorum” bir cevap değildir. Metindeki ipucunu yeniden incele.", "“I don't know” does not answer the question. Review the clue in the text.");
        if (!answer.Any(char.IsLetter))
            return Localized(language, "Bu cevap anlamlı bir sözcük içermiyor.", "This answer does not contain a meaningful word.");

        var culture = language == "tr"
            ? System.Globalization.CultureInfo.GetCultureInfo("tr-TR")
            : System.Globalization.CultureInfo.GetCultureInfo("en-US");
        var letters = new string(answer.Where(char.IsLetter).Select(character => char.ToLower(character, culture)).ToArray());
        var vowels = language == "tr" ? "aeıioöuüâîû" : "aeiouy";
        var maxConsonantRun = 0;
        var consonantRun = 0;
        foreach (var letter in letters)
        {
            consonantRun = vowels.Contains(letter) ? 0 : consonantRun + 1;
            maxConsonantRun = Math.Max(maxConsonantRun, consonantRun);
        }
        var rareLetters = language == "tr" ? letters.Count(letter => letter is 'q' or 'w' or 'x') : 0;
        var answerWords = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => new string(word.Where(char.IsLetter).ToArray()))
            .Where(word => word.Length > 0)
            .ToList();
        var hasPronounceableWord = answerWords.Any(word => word.Any(vowels.Contains));
        if (letters.Length <= 1
            || (letters.Length <= 5 && !hasPronounceableWord)
            || (language == "tr" && rareLetters >= 2)
            || (letters.Length >= 7 && rareLetters >= 2 && maxConsonantRun >= 5)
            || (letters.Length >= 6 && letters.Distinct().Count() <= 2))
            return Localized(language, "Bu cevap rastgele veya anlamsız karakterlerden oluşuyor.", "This answer appears to be random or meaningless text.");

        var normalizedPrompt = QuestionSetValidator.Normalize(prompt).TrimEnd('?', '.', '!');
        if (normalized.Length >= 12 && (normalized == normalizedPrompt || normalizedPrompt == normalized.TrimEnd('?', '.', '!')))
            return Localized(language, "Soru tekrar edilmiş, ancak cevap verilmemiş.", "The question was repeated without providing an answer.");
        return null;
    }

    private static string Localized(string language, string turkish, string english) => language == "tr" ? turkish : english;

    private static string EvaluationFeedback(
        string language,
        ComprehensionEvaluationStatus status,
        OpenAnswerEvaluation evaluation) => status switch
    {
        ComprehensionEvaluationStatus.Correct => Localized(language,
            "Doğru! Cevabın metindeki bilgiyle aynı anlamı taşıyor.",
            "Correct! Your answer has the same meaning as the information in the text."),
        ComprehensionEvaluationStatus.Partial => Localized(language,
            "Cevabında doğru bir ipucu var, ancak önemli bir ayrıntı eksik.",
            "Your answer includes a correct idea, but an important detail is missing."),
        ComprehensionEvaluationStatus.Incorrect when evaluation.ContradictsReference => Localized(language,
            "Bu cevap doğru değil; metindeki bilgiyle çelişiyor. Doğru cevabı ve kanıtı incele.",
            "This answer is not correct; it contradicts the text. Review the correct answer and evidence."),
        ComprehensionEvaluationStatus.Incorrect when !evaluation.IsRelevant => Localized(language,
            "Bu cevap soruyu yanıtlamıyor. Metindeki ilgili bölümü yeniden incele.",
            "This answer does not address the question. Review the relevant part of the text."),
        ComprehensionEvaluationStatus.Incorrect => Localized(language,
            "Bu cevap metindeki kanıtla desteklenmiyor. Doğru cevabı ve kanıtı incele.",
            "This answer is not supported by the text. Review the correct answer and evidence."),
        _ => Localized(language,
            "Bu cevap güvenilir biçimde değerlendirilemedi. Mentörün incelemesi gerekiyor.",
            "This answer could not be graded reliably. A mentor needs to review it.")
    };

    private static StudentAttempt ToStudentAttempt(ComprehensionTestAttempt attempt) => new(
        attempt.Id, attempt.TextId, attempt.TextTitle, attempt.Language, attempt.Status.ToString(), attempt.ModelId,
        attempt.TotalScore, attempt.MaxScore,
        attempt.Questions.OrderBy(question => question.Order).Select(question => new StudentQuestion(
            question.Id, question.Order,
            question.Type == ComprehensionQuestionType.MultipleChoice ? "multiple_choice" : "short_answer",
            question.Skill, question.Difficulty, question.Prompt,
            question.OptionsJson is null ? [] : JsonSerializer.Deserialize<List<string>>(question.OptionsJson) ?? [])).ToList(),
        attempt.Status == ComprehensionAttemptStatus.Failed ? attempt.FailureMessage : null);

    private static ComprehensionResult BuildExistingResult(ComprehensionTestAttempt attempt)
    {
        var feedback = attempt.Questions.OrderBy(question => question.Order).Select(question =>
        {
            var answer = question.Answer ?? throw new InvalidOperationException("Submitted attempt is missing an answer.");
            var status = answer.Status;
            var score = answer.AwardedPoints;
            var safeFeedback = answer.Feedback;
            if (question.Type == ComprehensionQuestionType.ShortAnswer
                && InvalidOpenAnswerReason(answer.TextAnswer, question.Prompt, attempt.Language) is { } invalidReason)
            {
                status = ComprehensionEvaluationStatus.Incorrect;
                score = 0;
                safeFeedback = invalidReason;
            }
            if ((status == ComprehensionEvaluationStatus.Correct && score != 1)
                || (status == ComprehensionEvaluationStatus.Partial && score != 0.5m)
                || (status is ComprehensionEvaluationStatus.Incorrect or ComprehensionEvaluationStatus.ReviewRequired && score != 0))
            {
                status = ComprehensionEvaluationStatus.ReviewRequired;
                score = 0;
            }
            if (!QuestionSetValidator.IsLanguageMatch(safeFeedback, attempt.Language))
                safeFeedback = Localized(attempt.Language,
                    "Bu sonucun açıklaması güvenilir dil doğrulamasından geçmedi.",
                    "The explanation for this result did not pass language validation.");
            var evidenceVerified = QuestionSetValidator.IsVerifiedEvidence(
                attempt.SourceText, question.Prompt, question.ReferenceAnswer, question.Evidence);
            if (!evidenceVerified && question.Type == ComprehensionQuestionType.ShortAnswer)
            {
                status = ComprehensionEvaluationStatus.ReviewRequired;
                score = 0;
            }
            var safeExplanation = QuestionSetValidator.IsLanguageMatch(question.Explanation, attempt.Language)
                ? question.Explanation
                : Localized(attempt.Language,
                    "Açıklama kaynak metnin diliyle uyuşmadığı için gösterilmedi.",
                    "The explanation was hidden because it did not match the source language.");
            return new AnswerFeedback(question.Id, status.ToString(), score, answer.Confidence,
                safeFeedback, safeExplanation, evidenceVerified ? question.Evidence : null, evidenceVerified,
                question.CorrectOptionIndex, question.ReferenceAnswer);
        }).ToList();
        var feedbackByQuestion = feedback.ToDictionary(item => item.QuestionId);
        var skills = attempt.Questions.GroupBy(question => question.Skill).Select(group => new SkillResult(
            group.Key, group.Sum(question => feedbackByQuestion[question.Id].Score), group.Count())).ToList();
        var reviewCount = feedback.Count(item => item.Status == ComprehensionEvaluationStatus.ReviewRequired.ToString());
        var isFinal = reviewCount == 0;
        decimal? safeTotal = isFinal ? feedback.Sum(item => item.Score) : null;
        return new ComprehensionResult(attempt.Id, safeTotal, attempt.MaxScore, isFinal,
            reviewCount, feedback, skills);
    }
}
