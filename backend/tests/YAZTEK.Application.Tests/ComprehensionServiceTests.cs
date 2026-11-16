using YAZTEK.Application.Comprehension;
using YAZTEK.Domain;
using Xunit;

namespace YAZTEK.Application.Tests;

public sealed class ComprehensionServiceTests
{
    [Fact]
    public void History_comparison_rejects_fake_rewording_but_allows_a_genuinely_different_angle()
    {
        const string previous = "Metne göre Elif pusulanın farklı yön göstermesinden ne anladı?";
        Assert.Equal("superficial", QuestionSetValidator.HistoryConflict(
            "Elif pusulanın farklı yön göstermesinden ne anladı acaba?", [previous]));
        Assert.Null(QuestionSetValidator.HistoryConflict(
            "Pusulanın davranışı Elif'in bu nesne hakkındaki düşüncesini nasıl değiştirdi?", [previous]));
    }

    [Fact]
    public void Ground_evidence_replaces_an_exact_but_irrelevant_source_sentence()
    {
        const string source = "Köylüler ormana Fısıldayan Orman adını verdi. Elif pusulanın kuzey yerine farklı yönü gösterdiğini fark etti.";
        var question = new GeneratedQuestion("short_answer", "inference", "medium",
            "Elif pusulanın sıra dışı olduğunu neden düşündü?", [], null,
            "Pusula kuzey yerine farklı yönü gösterdiği için.", "Pusulanın davranışı sıra dışıdır.",
            "Köylüler ormana Fısıldayan Orman adını verdi.", ["Nedeni açıklar"]);

        var grounded = QuestionSetValidator.GroundEvidence(question, source);

        Assert.Equal("Elif pusulanın kuzey yerine farklı yönü gösterdiğini fark etti.", grounded.Evidence);
        Assert.True(QuestionSetValidator.IsVerifiedEvidence(source, grounded.Prompt, grounded.Answer, grounded.Evidence));
    }

    [Fact]
    public async Task Two_attempts_are_unique_old_answers_survive_scoring_is_correct_and_two_languages_work()
    {
        var store = new MemoryAttemptStore();
        var ai = new DeterministicAiClient();
        var service = new ComprehensionService(store, ai);
        const string turkishText = "Kırlangıçlar bahçedeki erik ağacına yuva yaptı. Yavrular sabah annelerini bekledi. Güçlü rüzgâr yuvayı sallayınca küçük yavru korktu.";

        var first = await service.CreateAttemptAsync(new("student-1", "text-1", "Kırlangıçlar", turkishText, Guid.NewGuid().ToString()), default);
        Assert.Equal("tr", first.Language);
        AssertSeven(first);
        var firstResult = await service.SubmitAsync(first.Id, CorrectSubmission(first, "student-1", store), default);
        Assert.Equal(7m, firstResult.TotalScore);

        var second = await service.CreateAttemptAsync(new("student-1", "text-1", "Kırlangıçlar", turkishText, Guid.NewGuid().ToString()), default);
        AssertSeven(second);
        Assert.Empty(first.Questions.Select(question => question.Prompt).Intersect(second.Questions.Select(question => question.Prompt)));
        Assert.Equal(7, store.Attempts.Single(attempt => attempt.Id == first.Id).Questions.Count(question => question.Answer is not null));

        const string englishText = "The children planted seeds beside the school. They watered the soil every morning, so green shoots appeared after one week and attracted butterflies.";
        var english = await service.CreateAttemptAsync(new("student-1", "text-en", "School Garden", englishText, Guid.NewGuid().ToString()), default);
        Assert.Equal("en", english.Language);
        AssertSeven(english);
    }

    [Fact]
    public async Task Same_idempotency_key_returns_one_attempt_and_does_not_call_model_twice()
    {
        var store = new MemoryAttemptStore();
        var ai = new DeterministicAiClient();
        var service = new ComprehensionService(store, ai);
        const string text = "Bir çocuk her gün kütüphaneye giderek doğa kitapları okudu. Öğrendiklerini arkadaşlarıyla paylaşınca sınıf birlikte bir bahçe kurdu.";
        var key = Guid.NewGuid().ToString();
        var first = await service.CreateAttemptAsync(new("student-1", "text-1", "Bahçe", text, key), default);
        var duplicate = await service.CreateAttemptAsync(new("student-1", "text-1", "Bahçe", text, key), default);
        Assert.Equal(first.Id, duplicate.Id);
        Assert.Equal(1, ai.GenerationCalls);
    }

    [Fact]
    public async Task Rejected_quality_review_is_not_published_and_is_regenerated_once()
    {
        var store = new MemoryAttemptStore();
        var ai = new DeterministicAiClient(rejectFirstReview: true);
        var service = new ComprehensionService(store, ai);
        const string text = "Öğrenciler yağmur suyunu bir kapta topladı. Bu suyu okul bahçesindeki fidelerde kullanınca hem su tasarrufu yaptılar hem bitkileri büyüttüler.";
        var attempt = await service.CreateAttemptAsync(new("student-1", "text-quality", "Yağmur Suyu", text, Guid.NewGuid().ToString()), default);
        AssertSeven(attempt);
        Assert.Equal(2, ai.GenerationCalls);
        Assert.Equal(2, ai.ReviewCalls);
    }

    [Fact]
    public async Task Only_repeated_questions_are_regenerated_with_their_original_types()
    {
        var store = new MemoryAttemptStore();
        var ai = new DeterministicAiClient { RepeatFirstThreeOnNextAttempt = true, InvalidFirstRepairAlternatives = true };
        var service = new ComprehensionService(store, ai);
        const string text = "Rüzgâr yaprakların arasından geçerken fısıltıya benzeyen sesler çıkarıyordu. Köylüler bu yüzden ormana Fısıldayan Orman adını verdi. Elif eski pusulanın farklı yönü gösterdiğini fark etti.";
        var first = await service.CreateAttemptAsync(new("student-repair", "forest", "Fısıldayan Orman", text, Guid.NewGuid().ToString()), default);
        var second = await service.CreateAttemptAsync(new("student-repair", "forest", "Fısıldayan Orman", text, Guid.NewGuid().ToString()), default);

        AssertSeven(second);
        Assert.Empty(first.Questions.Select(question => question.Prompt).Intersect(second.Questions.Select(question => question.Prompt)));
        Assert.Equal(4, ai.GenerationCalls);
        Assert.Equal([(4, 0), (2, 0)], ai.GenerationShapes.Skip(2).ToList());
        Assert.Equal(2, ai.ReviewCalls);
    }

    [Fact]
    public async Task Superficial_duplicate_inside_the_same_set_is_replaced_without_recreating_valid_questions()
    {
        var store = new MemoryAttemptStore();
        var ai = new DeterministicAiClient { RepeatWithinFirstDraft = true };
        var service = new ComprehensionService(store, ai);
        const string text = "Rüzgâr yaprakların arasından geçerken fısıltıya benzeyen sesler çıkarıyordu. Köylüler bu yüzden ormana Fısıldayan Orman adını verdi. Elif eski pusulanın farklı yönü gösterdiğini fark etti.";

        var attempt = await service.CreateAttemptAsync(new("student-current-set", "forest", "Fısıldayan Orman", text, Guid.NewGuid().ToString()), default);

        AssertSeven(attempt);
        Assert.Equal(2, ai.GenerationCalls);
        Assert.Equal([(4, 3), (0, 2)], ai.GenerationShapes);
        Assert.Equal(1, ai.ReviewCalls);
    }

    [Fact]
    public async Task Repeated_repair_output_stops_after_the_bounded_generation_budget()
    {
        var store = new MemoryAttemptStore();
        var ai = new DeterministicAiClient { RepeatFirstThreeOnNextAttempt = true, RepeatEveryRepair = true };
        var service = new ComprehensionService(store, ai);
        const string text = "Rüzgâr yaprakların arasından geçerken fısıltıya benzeyen sesler çıkarıyordu. Köylüler bu yüzden ormana Fısıldayan Orman adını verdi. Elif eski pusulanın farklı yönü gösterdiğini fark etti.";
        await service.CreateAttemptAsync(new("student-bounded", "forest", "Fısıldayan Orman", text, Guid.NewGuid().ToString()), default);

        var error = await Assert.ThrowsAsync<ComprehensionServiceException>(() => service.CreateAttemptAsync(
            new("student-bounded", "forest", "Fısıldayan Orman", text, Guid.NewGuid().ToString()), default));

        Assert.Equal("question_quality_failed", error.Code);
        Assert.Contains("önceki bir denemeyle birebir aynı", error.Message);
        Assert.Equal(4, ai.GenerationCalls); // first attempt: 1; second attempt: at most 3
        Assert.Equal(ComprehensionAttemptStatus.Failed, store.Attempts.Last().Status);
    }

    [Fact]
    public async Task Random_blank_and_unknown_short_answers_receive_zero_without_calling_ai()
    {
        var store = new MemoryAttemptStore();
        var ai = new DeterministicAiClient();
        var service = new ComprehensionService(store, ai);
        const string text = "Rüzgâr yaprakların arasından geçerken fısıltıya benzeyen sesler çıkarıyordu. Bu nedenle köylüler buraya Fısıldayan Orman adını verdi. Elif bu sesi dikkatle dinledi.";
        var attempt = await service.CreateAttemptAsync(new("student-invalid", "forest", "Fısıldayan Orman", text, Guid.NewGuid().ToString()), default);
        var shortAnswers = new Queue<string?>(["wnqjfknqe", "", "Bilmiyorum"]);
        var submission = new SubmitComprehensionAttempt("student-invalid", attempt.Questions.Select(question =>
            new SubmittedAnswerInput(question.Id, question.Type == "multiple_choice" ? 0 : null,
                question.Type == "short_answer" ? shortAnswers.Dequeue() : null)).ToList());

        var result = await service.SubmitAsync(attempt.Id, submission, default);

        Assert.True(result.IsFinal);
        Assert.Equal(4m, result.TotalScore);
        Assert.All(result.Answers.Skip(4), answer =>
        {
            Assert.Equal("Incorrect", answer.Status);
            Assert.Equal(0m, answer.Score);
        });
        Assert.Equal(0, ai.EvaluationCalls);
    }

    [Fact]
    public async Task Very_short_consonant_noise_receives_zero_without_calling_ai()
    {
        var store = new MemoryAttemptStore();
        var ai = new DeterministicAiClient();
        var service = new ComprehensionService(store, ai);
        const string text = "Yavrular her sabah cıvıl cıvıl öterek annelerini bekledi. Güçlü rüzgâr yuvayı sallayınca en küçük yavru korktu. Anne kırlangıç kısa süre sonra yuvaya döndü.";
        var attempt = await service.CreateAttemptAsync(new("student-noise", "birds", "Kırlangıçlar", text, Guid.NewGuid().ToString()), default);
        var noise = new Queue<string>(["vsd", "vds", "bbk"]);
        var submission = new SubmitComprehensionAttempt("student-noise", attempt.Questions.Select(question =>
            new SubmittedAnswerInput(question.Id, question.Type == "multiple_choice" ? 0 : null,
                question.Type == "short_answer" ? noise.Dequeue() : null)).ToList());

        var result = await service.SubmitAsync(attempt.Id, submission, default);

        Assert.True(result.IsFinal);
        Assert.Equal(4m, result.TotalScore);
        Assert.All(result.Answers.Skip(4), answer =>
        {
            Assert.Equal("Incorrect", answer.Status);
            Assert.Equal(0m, answer.Score);
            Assert.Contains("anlamsız", answer.Feedback);
        });
        Assert.Equal(0, ai.EvaluationCalls);
    }

    [Fact]
    public async Task Model_cannot_rubber_stamp_an_unrelated_answer_as_correct()
    {
        var store = new MemoryAttemptStore();
        var ai = new DeterministicAiClient { EvaluationMode = "correct" };
        var service = new ComprehensionService(store, ai);
        const string text = "Rüzgâr yaprakların arasından geçerken fısıltıya benzeyen sesler çıkarıyordu. Bu nedenle köylüler buraya Fısıldayan Orman adını verdi. Elif bu sesi dikkatle dinledi.";
        var attempt = await service.CreateAttemptAsync(new("student-unrelated", "forest-unrelated", "Fısıldayan Orman", text, Guid.NewGuid().ToString()), default);
        var submission = new SubmitComprehensionAttempt("student-unrelated", attempt.Questions.Select(question =>
            new SubmittedAnswerInput(question.Id, question.Type == "multiple_choice" ? 0 : null,
                question.Type == "short_answer" ? "Gökyüzü bugün masmavi ve deniz çok sakindir." : null)).ToList());

        var result = await service.SubmitAsync(attempt.Id, submission, default);

        Assert.False(result.IsFinal);
        Assert.Null(result.TotalScore);
        Assert.All(result.Answers.Skip(4), answer =>
        {
            Assert.NotEqual("Correct", answer.Status);
            Assert.Equal(0m, answer.Score);
        });
    }

    [Fact]
    public async Task Contradictory_model_fields_never_award_points_or_finalize_score()
    {
        var store = new MemoryAttemptStore();
        var ai = new DeterministicAiClient { EvaluationMode = "contradictory" };
        var service = new ComprehensionService(store, ai);
        const string text = "Pusulanın ibresi kuzeyi göstermek yerine titreyerek farklı bir yönü işaret ediyordu. Elif bu yüzden pusulanın sıradan bir yön bulma aracı olmadığını anladı.";
        var attempt = await service.CreateAttemptAsync(new("student-review", "compass", "Pusula", text, Guid.NewGuid().ToString()), default);

        var result = await service.SubmitAsync(attempt.Id, CorrectSubmission(attempt, "student-review", store), default);

        Assert.False(result.IsFinal);
        Assert.Null(result.TotalScore);
        Assert.Equal(3, result.ReviewRequiredCount);
        Assert.All(result.Answers.Skip(4), answer => Assert.Equal(0m, answer.Score));
        Assert.Equal(ComprehensionAttemptStatus.ReviewRequired, store.Attempts.Single().Status);
    }

    [Fact]
    public async Task Semantic_alternative_partial_and_incorrect_results_keep_status_and_score_consistent()
    {
        const string text = "Rüzgâr yaprakların arasından geçerken fısıltıya benzeyen sesler çıkarıyordu. Bu nedenle köylüler buraya Fısıldayan Orman adını verdi. Elif bu sesi dikkatle dinledi.";
        foreach (var (mode, expectedStatus, expectedScore) in new[]
        {
            ("correct", "Correct", 1m),
            ("partial", "Partial", 0.5m),
            ("incorrect", "Incorrect", 0m)
        })
        {
            var store = new MemoryAttemptStore();
            var ai = new DeterministicAiClient { EvaluationMode = mode };
            var service = new ComprehensionService(store, ai);
            var attempt = await service.CreateAttemptAsync(new("student-semantic", $"forest-{mode}", "Fısıldayan Orman", text, Guid.NewGuid().ToString()), default);
            var result = await service.SubmitAsync(attempt.Id, CorrectSubmission(attempt, "student-semantic", store), default);
            Assert.All(result.Answers.Skip(4), answer =>
            {
                Assert.Equal(expectedStatus, answer.Status);
                Assert.Equal(expectedScore, answer.Score);
                Assert.True(answer.EvidenceVerified);
            });
        }
    }

    [Fact]
    public async Task Evaluation_failure_is_review_required_and_does_not_finalize_report_score()
    {
        var store = new MemoryAttemptStore();
        var ai = new DeterministicAiClient { EvaluationMode = "throw" };
        var service = new ComprehensionService(store, ai);
        const string text = "The compass needle trembled and pointed away from north. Elif therefore understood that it was not an ordinary navigation tool. She showed it to her friend.";
        var attempt = await service.CreateAttemptAsync(new("student-timeout", "compass-en", "Compass", text, Guid.NewGuid().ToString()), default);
        var result = await service.SubmitAsync(attempt.Id, CorrectSubmission(attempt, "student-timeout", store), default);
        Assert.False(result.IsFinal);
        Assert.Null(result.TotalScore);
        Assert.All(result.Answers.Skip(4), answer => Assert.Equal("ReviewRequired", answer.Status));
        Assert.All(result.Answers.Skip(4), answer => Assert.Contains("could not be graded reliably", answer.Feedback));
    }

    [Fact]
    public async Task Evidence_that_is_not_an_exact_relevant_source_quote_is_hidden_and_requires_review()
    {
        var store = new MemoryAttemptStore();
        var ai = new DeterministicAiClient();
        var service = new ComprehensionService(store, ai);
        const string text = "Rüzgâr yaprakların arasından geçerken fısıltıya benzeyen sesler çıkarıyordu. Köylüler bu nedenle ormana Fısıldayan Orman adını verdi. Elif sesi dikkatle dinledi.";
        var attempt = await service.CreateAttemptAsync(new("student-evidence", "forest-evidence", "Fısıldayan Orman", text, Guid.NewGuid().ToString()), default);
        foreach (var question in store.Attempts.Single().Questions.Where(question => question.Type == ComprehensionQuestionType.ShortAnswer))
            question.Evidence = "Bu cümle kaynak metinde yoktur ve soruyu desteklemez.";

        var result = await service.SubmitAsync(attempt.Id, CorrectSubmission(attempt, "student-evidence", store), default);

        Assert.False(result.IsFinal);
        Assert.Null(result.TotalScore);
        Assert.All(result.Answers.Skip(4), answer =>
        {
            Assert.Equal("ReviewRequired", answer.Status);
            Assert.False(answer.EvidenceVerified);
            Assert.Null(answer.Evidence);
        });
    }

    private static void AssertSeven(StudentAttempt attempt)
    {
        Assert.Equal(7, attempt.Questions.Count);
        Assert.Equal(4, attempt.Questions.Count(question => question.Type == "multiple_choice"));
        Assert.Equal(3, attempt.Questions.Count(question => question.Type == "short_answer"));
        Assert.True(attempt.Questions.Select(question => question.Skill).Distinct().Count() >= 4);
    }

    private static SubmitComprehensionAttempt CorrectSubmission(
        StudentAttempt attempt,
        string studentId,
        MemoryAttemptStore store)
    {
        var storedAttempt = store.Attempts.Single(item => item.Id == attempt.Id);
        return new SubmitComprehensionAttempt(
            studentId,
            attempt.Questions.Select(question =>
            {
                var storedQuestion = storedAttempt.Questions.Single(item => item.Id == question.Id);
                return new SubmittedAnswerInput(
                    question.Id,
                    question.Type == "multiple_choice" ? 0 : null,
                    question.Type == "short_answer" ? storedQuestion.ReferenceAnswer : null);
            }).ToList());
    }

    private sealed class DeterministicAiClient(bool rejectFirstReview = false) : IComprehensionAiClient
    {
        public string ModelId => "deterministic-test-double";
        public int GenerationCalls { get; private set; }
        public int ReviewCalls { get; private set; }
        public int EvaluationCalls { get; private set; }
        public string EvaluationMode { get; init; } = "correct";
        public bool RepeatFirstThreeOnNextAttempt { get; init; }
        public bool RepeatEveryRepair { get; init; }
        public bool InvalidFirstRepairAlternatives { get; init; }
        public bool RepeatWithinFirstDraft { get; init; }
        public List<(int MultipleChoice, int ShortAnswer)> GenerationShapes { get; } = [];
        private bool _repeatedDraftServed;
        private bool _invalidRepairServed;
        private bool _withinDraftRepeatServed;

        public Task<ComprehensionAiHealth> GetHealthAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ComprehensionAiHealth(true, "Test", ModelId, "ready", null));

        public Task<IReadOnlyList<GeneratedQuestion>> GenerateQuestionsAsync(GenerationRequest request, CancellationToken cancellationToken)
        {
            GenerationCalls++;
            GenerationShapes.Add((request.MultipleChoiceCount, request.ShortAnswerCount));
            var variant = request.PreviousQuestions.Count + GenerationCalls;
            var generationIndex = Math.Min(GenerationCalls - 1, 5);
            var generationWord = (request.Language == "tr"
                ? new[] { "birinci", "ikinci", "üçüncü", "dördüncü", "beşinci", "altıncı" }
                : new[] { "first", "second", "third", "fourth", "fifth", "sixth" })[generationIndex];
            var evidence = request.SourceText.Split('.', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
            var skillSet = new[] { "main_idea", "key_detail", "cause_effect", "inference", "character_event_relationship", "context_meaning", "general_evaluation" };
            var types = Enumerable.Repeat("multiple_choice", request.MultipleChoiceCount)
                .Concat(Enumerable.Repeat("short_answer", request.ShortAnswerCount))
                .ToList();
            var questions = types.Select((type, index) => new GeneratedQuestion(
                type,
                skillSet[index % skillSet.Length], index < 2 ? "easy" : "medium",
                request.Language == "tr"
                    ? $"{generationWord} yaklaşımda {skillSet[index % skillSet.Length]} için doğru bilgi nedir, çeşit {variant}?"
                    : $"In the {generationWord} approach, what fact supports {skillSet[index % skillSet.Length]}, variant {variant}?",
                type == "multiple_choice" ? [evidence, "Yanlış seçenek A", "Yanlış seçenek B", "Yanlış seçenek C"] : [],
                type == "multiple_choice" ? 0 : null,
                evidence,
                request.Language == "tr" ? "Doğru cevap metindeki bu cümleyle desteklenir." : "The correct answer is supported by this sentence in the text.",
                evidence,
                ["Temel bilgiyi doğru ifade eder", "Metindeki kanıtla çelişmez"])).ToList();
            if (RepeatWithinFirstDraft && !_withinDraftRepeatServed && types.Count == 7)
            {
                questions[5] = questions[5] with { Prompt = questions[4].Prompt + " acaba?" };
                _withinDraftRepeatServed = true;
            }
            else if (RepeatFirstThreeOnNextAttempt && !_repeatedDraftServed
                && request.PreviousQuestions.Count >= 3 && types.Count == 7)
            {
                for (var index = 0; index < 3; index++)
                    questions[index] = questions[index] with { Prompt = request.PreviousQuestions[index] };
                _repeatedDraftServed = true;
            }
            else if (RepeatEveryRepair && types.Count < 7 && request.PreviousQuestions.Count >= types.Count)
            {
                for (var index = 0; index < types.Count; index++)
                    questions[index] = questions[index] with { Prompt = request.PreviousQuestions[index] };
            }
            else if (InvalidFirstRepairAlternatives && !_invalidRepairServed
                && types.Count < 7 && request.PreviousQuestions.Count >= 2)
            {
                questions[0] = questions[0] with { Prompt = request.PreviousQuestions[0] };
                questions[1] = questions[1] with { Prompt = request.PreviousQuestions[1] };
                _invalidRepairServed = true;
            }
            return Task.FromResult<IReadOnlyList<GeneratedQuestion>>(questions);
        }

        public Task<QuestionSetReview> ReviewQuestionSetAsync(GenerationRequest request, IReadOnlyList<GeneratedQuestion> questions, CancellationToken cancellationToken)
        {
            ReviewCalls++;
            return Task.FromResult(rejectFirstReview && ReviewCalls == 1
                ? new QuestionSetReview(false, ["İlk taslak kalite kontrolünden geçmedi."])
                : new QuestionSetReview(true, []));
        }

        public Task<IReadOnlyList<OpenAnswerEvaluation>> EvaluateAnswersAsync(IReadOnlyList<OpenAnswerEvaluationRequest> requests, CancellationToken cancellationToken)
        {
            EvaluationCalls++;
            if (EvaluationMode == "throw") throw new TimeoutException("simulated timeout");
            return Task.FromResult<IReadOnlyList<OpenAnswerEvaluation>>(requests.Select(request => EvaluationMode switch
            {
                "partial" => new OpenAnswerEvaluation(request.QuestionId, "partial", 0.5m, 0.9m, true, true, false,
                    request.Language == "tr" ? "Cevap doğru bilginin yalnızca bir bölümünü açıklıyor." : "The answer explains only part of the correct information.", request.Evidence),
                "incorrect" => new OpenAnswerEvaluation(request.QuestionId, "incorrect", 0m, 0.95m, false, false, true,
                    request.Language == "tr" ? "Bu cevap metindeki bilgiyle çelişiyor." : "This answer contradicts the information in the text.", request.Evidence),
                "contradictory" => new OpenAnswerEvaluation(request.QuestionId, "correct", 1m, 0.95m, false, false, true,
                    request.Language == "tr" ? "Bu cevap metindeki bilgiyle çelişiyor." : "This answer contradicts the information in the text.", request.Evidence),
                _ => new OpenAnswerEvaluation(request.QuestionId, "correct", 1m, 0.95m, true, true, false,
                    request.Language == "tr" ? "Doğru! Cevabın metindeki bilgiyle aynı anlamı taşıyor." : "Correct! Your answer has the same meaning as the text.", request.Evidence)
            }).ToList());
        }
    }

    private sealed class MemoryAttemptStore : IComprehensionAttemptStore
    {
        public List<ComprehensionTestAttempt> Attempts { get; } = [];

        public Task<AttemptContext> GetOrCreateAsync(ComprehensionTestAttempt attempt, CancellationToken cancellationToken)
        {
            var existing = Attempts.SingleOrDefault(item => item.StudentId == attempt.StudentId && item.TextId == attempt.TextId && item.IdempotencyKey == attempt.IdempotencyKey);
            if (existing is not null) return Task.FromResult(new AttemptContext(existing, false, [], []));
            var previousAttempts = Attempts.Where(item => item.StudentId == attempt.StudentId && item.TextId == attempt.TextId && item.SourceTextHash == attempt.SourceTextHash).ToList();
            var previous = previousAttempts.SelectMany(item => item.Questions).Select(question => question.Prompt).ToList();
            var weak = previousAttempts.SelectMany(item => item.Questions).Where(question => question.Answer?.AwardedPoints < 0.75m).Select(question => question.Skill).Distinct().ToList();
            Attempts.Add(attempt);
            return Task.FromResult(new AttemptContext(attempt, true, previous, weak));
        }

        public Task SaveGeneratedQuestionsAsync(Guid attemptId, IReadOnlyList<ComprehensionQuestion> questions, CancellationToken cancellationToken)
        {
            var attempt = Attempts.Single(item => item.Id == attemptId);
            attempt.Questions = questions.ToList();
            foreach (var question in attempt.Questions) question.Attempt = attempt;
            attempt.Status = ComprehensionAttemptStatus.Ready;
            return Task.CompletedTask;
        }

        public Task MarkFailedAsync(Guid attemptId, string code, string message, CancellationToken cancellationToken)
        {
            var attempt = Attempts.Single(item => item.Id == attemptId);
            attempt.Status = ComprehensionAttemptStatus.Failed; attempt.FailureCode = code; attempt.FailureMessage = message;
            return Task.CompletedTask;
        }

        public Task<ComprehensionTestAttempt?> GetForStudentAsync(Guid attemptId, string studentId, CancellationToken cancellationToken) =>
            Task.FromResult(Attempts.SingleOrDefault(item => item.Id == attemptId && item.StudentId == studentId));

        public Task SaveAnswersAsync(Guid attemptId, IReadOnlyList<ComprehensionAnswer> answers, decimal? totalScore, CancellationToken cancellationToken)
        {
            var attempt = Attempts.Single(item => item.Id == attemptId);
            foreach (var answer in answers)
            {
                var question = attempt.Questions.Single(item => item.Id == answer.QuestionId);
                question.Answer = answer; answer.Question = question;
            }
            attempt.TotalScore = totalScore;
            attempt.Status = totalScore.HasValue ? ComprehensionAttemptStatus.Submitted : ComprehensionAttemptStatus.ReviewRequired;
            attempt.SubmittedAtUtc = DateTimeOffset.UtcNow;
            return Task.CompletedTask;
        }
    }
}
