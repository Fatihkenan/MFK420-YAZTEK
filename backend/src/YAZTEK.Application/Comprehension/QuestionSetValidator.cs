using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using YAZTEK.Domain;

namespace YAZTEK.Application.Comprehension;

public static partial class QuestionSetValidator
{
    public static IReadOnlyList<string> Validate(
        IReadOnlyList<GeneratedQuestion> questions,
        string sourceText,
        IReadOnlyCollection<string> previousQuestions)
    {
        var errors = new List<string>();
        if (questions.Count != 7) errors.Add("Tam olarak 7 soru üretilmelidir.");
        if (questions.Count(question => IsMultipleChoice(question.Type)) != 4) errors.Add("Tam olarak 4 çoktan seçmeli soru olmalıdır.");
        if (questions.Count(question => IsShortAnswer(question.Type)) != 3) errors.Add("Tam olarak 3 açık uçlu soru olmalıdır.");

        var current = new HashSet<string>(StringComparer.Ordinal);
        var currentPrompts = new List<string>();
        foreach (var (question, index) in questions.Select((item, index) => (item, index)))
        {
            var prefix = $"Soru {index + 1}:";
            var normalized = Normalize(question.Prompt);
            if (normalized.Length < 12) errors.Add($"{prefix} soru metni çok kısa.");
            if (!current.Add(normalized)) errors.Add($"{prefix} bu sette yineleniyor.");
            else if (currentPrompts.Any(existing => IsSuperficialRewrite(question.Prompt, existing)))
                errors.Add($"{prefix} aynı setteki başka bir sorudan yalnızca yüzeysel kelime değişiklikleriyle ayrılıyor.");
            currentPrompts.Add(question.Prompt);
            var historyConflict = HistoryConflict(question.Prompt, previousQuestions);
            if (historyConflict == "exact") errors.Add($"{prefix} önceki bir denemeyle birebir aynı.");
            else if (historyConflict == "superficial")
                errors.Add($"{prefix} önceki bir sorudan yalnızca yüzeysel kelime değişiklikleriyle ayrılıyor.");
            if (string.IsNullOrWhiteSpace(question.Skill) || string.IsNullOrWhiteSpace(question.Difficulty)) errors.Add($"{prefix} beceri veya zorluk eksik.");
            if (string.IsNullOrWhiteSpace(question.Answer) || string.IsNullOrWhiteSpace(question.Explanation)) errors.Add($"{prefix} cevap veya açıklama eksik.");
            if (string.IsNullOrWhiteSpace(question.Evidence) || !ContainsEvidence(sourceText, question.Evidence)) errors.Add($"{prefix} kanıt metinde birebir bulunmuyor.");
            else if (!IsEvidenceRelevant(question.Prompt, question.Answer, question.Evidence)) errors.Add($"{prefix} kanıt soruyu ve doğru cevabı desteklemiyor.");
            var expectedLanguage = DetectLanguage(sourceText);
            if (!IsLanguageMatch(question.Prompt, expectedLanguage) || !IsLanguageMatch(question.Explanation, expectedLanguage))
                errors.Add($"{prefix} soru veya açıklama metnin diliyle uyuşmuyor.");
            if (question.EvaluationCriteria.Count == 0 || question.EvaluationCriteria.Any(string.IsNullOrWhiteSpace)) errors.Add($"{prefix} değerlendirme ölçütü eksik.");

            if (IsMultipleChoice(question.Type))
            {
                if (question.Options.Count != 4) errors.Add($"{prefix} dört seçenek içermeli.");
                if (question.Options.Select(Normalize).Distinct(StringComparer.Ordinal).Count() != question.Options.Count) errors.Add($"{prefix} seçenekler benzersiz olmalı.");
                if (question.CorrectOptionIndex is not int correctIndex || correctIndex is < 0 or > 3) errors.Add($"{prefix} doğru seçenek indeksi geçersiz.");
                else if (question.Options.Count == 4 && !string.Equals(
                    Normalize(question.Options[correctIndex]),
                    Normalize(question.Answer),
                    StringComparison.Ordinal)) errors.Add($"{prefix} doğru cevap anahtarı referans cevapla eşleşmiyor.");
            }
            else if (IsShortAnswer(question.Type))
            {
                if (question.Options.Count != 0 || question.CorrectOptionIndex is not null) errors.Add($"{prefix} açık uçlu soru seçenek veya seçenek indeksi içermemeli.");
            }
            else
            {
                errors.Add($"{prefix} soru tipi geçersiz.");
            }
        }

        if (questions.Select(question => Normalize(question.Skill)).Distinct().Count() < 4)
        {
            errors.Add("Soru seti en az dört farklı anlama becerisini kapsamalıdır.");
        }

        return errors;
    }

    public static GeneratedQuestion GroundEvidence(GeneratedQuestion question, string sourceText)
    {
        if (ContainsEvidence(sourceText, question.Evidence)
            && IsEvidenceRelevant(question.Prompt, question.Answer, question.Evidence)) return question;
        var targetTokens = SignificantTokens($"{question.Prompt} {question.Answer}");
        if (targetTokens.Count == 0) return question;

        var best = SentenceBoundaries().Split(sourceText.Trim())
            .Where(sentence => !string.IsNullOrWhiteSpace(sentence))
            .Select(sentence => new
            {
                Sentence = sentence.Trim(),
                Score = EvidenceOverlap(targetTokens, SignificantTokens(sentence))
            })
            .Where(item => IsEvidenceRelevant(question.Prompt, question.Answer, item.Sentence))
            .OrderByDescending(item => item.Score)
            .FirstOrDefault();
        return best is not null
            ? question with { Evidence = best.Sentence }
            : question;
    }

    public static IReadOnlyList<string> ValidateReplacement(
        GeneratedQuestion question,
        string sourceText,
        IReadOnlyCollection<string> forbiddenQuestions) =>
        Validate([question], sourceText, forbiddenQuestions)
            .Where(error => error.StartsWith("Soru 1:", StringComparison.Ordinal))
            .ToList();

    public static IReadOnlyList<string> EvidenceCandidates(string sourceText)
    {
        var sentences = SentenceBoundaries().Split(sourceText.Trim())
            .Select(sentence => sentence.Trim())
            .Where(sentence => sentence.Length is >= 3 and <= 500)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (sentences.Count > 0) return sentences;
        return [sourceText.Trim()[..Math.Min(sourceText.Trim().Length, 500)]];
    }

    public static ComprehensionQuestion ToEntity(GeneratedQuestion question, Guid attemptId, int order)
    {
        var type = IsMultipleChoice(question.Type)
            ? ComprehensionQuestionType.MultipleChoice
            : ComprehensionQuestionType.ShortAnswer;
        return new ComprehensionQuestion
        {
            Id = Guid.NewGuid(),
            AttemptId = attemptId,
            Order = order,
            Type = type,
            Skill = question.Skill.Trim(),
            Difficulty = question.Difficulty.Trim(),
            Prompt = question.Prompt.Trim(),
            OptionsJson = type == ComprehensionQuestionType.MultipleChoice ? JsonSerializer.Serialize(question.Options) : null,
            CorrectOptionIndex = question.CorrectOptionIndex,
            ReferenceAnswer = question.Answer.Trim(),
            Explanation = question.Explanation.Trim(),
            Evidence = question.Evidence.Trim(),
            RubricJson = JsonSerializer.Serialize(question.EvaluationCriteria),
            NormalizedPromptHash = HashPrompt(question.Prompt)
        };
    }

    public static string DetectLanguage(string text)
    {
        var normalized = $" {Normalize(text)} ";
        var turkishScore = TurkishCharacters().Matches(text).Count * 2
            + new[] { " ve ", " bir ", " bu ", " için ", " ile ", " sonra ", " çünkü ", " ancak ", " cevap ", " yanıt ", " doğru ", " değil ", " metin ", " soru " }.Count(normalized.Contains);
        var englishScore = new[] { " the ", " and ", " is ", " are ", " was ", " were ", " because ", " with ", " this ", " answer ", " correct ", " not ", " text ", " question ", " your ", " could " }.Count(normalized.Contains);
        return turkishScore >= englishScore ? "tr" : "en";
    }

    public static string HashPrompt(string prompt) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(NormalizePromptForComparison(prompt))));

    public static string Normalize(string value) => Whitespace().Replace(value.Trim().ToLower(CultureInfo.InvariantCulture), " ");

    public static IReadOnlyList<int> RepairableQuestionIndexes(IReadOnlyList<string> errors) => errors
        .Select(error => QuestionNumberPrefix().Match(error))
        .Where(match => match.Success && int.TryParse(match.Groups[1].Value, out _))
        .Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) - 1)
        .Where(index => index is >= 0 and < 7)
        .Distinct()
        .Order()
        .ToList();

    public static string? HistoryConflict(string prompt, IReadOnlyCollection<string> previousQuestions)
    {
        var normalized = NormalizePromptForComparison(prompt);
        if (previousQuestions.Any(previous => NormalizePromptForComparison(previous) == normalized)) return "exact";
        return previousQuestions.Any(previous => IsSuperficialRewrite(prompt, previous)) ? "superficial" : null;
    }

    public static bool IsLanguageMatch(string value, string expectedLanguage) =>
        !string.IsNullOrWhiteSpace(value) && DetectLanguage(value) == expectedLanguage;

    public static bool IsVerifiedEvidence(string sourceText, string prompt, string referenceAnswer, string evidence) =>
        !string.IsNullOrWhiteSpace(evidence)
        && ContainsEvidence(sourceText, evidence)
        && IsEvidenceRelevant(prompt, referenceAnswer, evidence);

    public static bool HasGroundedAnswerContent(
        string studentAnswer,
        string referenceAnswer,
        string evidence)
    {
        var answerTokens = SignificantTokens(studentAnswer);
        var expectedTokens = SignificantTokens($"{referenceAnswer} {evidence}");
        if (answerTokens.Count == 0 || expectedTokens.Count == 0) return false;
        return answerTokens.Any(answerToken =>
            expectedTokens.Any(expectedToken => AreMorphologicallyRelated(answerToken, expectedToken)));
    }

    private static bool ContainsEvidence(string source, string evidence) =>
        NormalizeEvidence(source).Contains(NormalizeEvidence(evidence), StringComparison.Ordinal);

    private static string NormalizeEvidence(string value) =>
        Whitespace().Replace(EvidenceSeparators().Replace(value.Trim().ToLower(CultureInfo.InvariantCulture), " "), " ");

    private static HashSet<string> EvidenceTokens(string value) =>
        NormalizeEvidence(value).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);

    private static bool IsEvidenceRelevant(string prompt, string answer, string evidence)
    {
        var target = SignificantTokens($"{prompt} {answer}");
        var evidenceTokens = SignificantTokens(evidence);
        var shared = target.Intersect(evidenceTokens, StringComparer.Ordinal).ToList();
        return shared.Count >= 2 || shared.Any(token => token.Length >= 7);
    }

    private static HashSet<string> SignificantTokens(string value)
    {
        var stopWords = new HashSet<string>(
            ["acaba", "olan", "olarak", "için", "ile", "bir", "bu", "şu", "ne", "nedir", "hangi", "neden", "nasıl", "metin", "metinde",
             "cevap", "yanıt", "doğru", "yanlış", "evet", "hayır", "yok", "var", "tamam",
             "the", "a", "an", "is", "are", "was", "were", "what", "which", "why", "how", "for", "with", "this", "that", "text",
             "answer", "correct", "incorrect", "yes", "no", "okay"],
            StringComparer.Ordinal);
        return EvidenceTokens(value)
            .Where(token => token.Length >= 3 && !stopWords.Contains(token))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static bool AreMorphologicallyRelated(string left, string right)
    {
        if (string.Equals(left, right, StringComparison.Ordinal)) return true;
        if (left.Length < 5 || right.Length < 5) return false;
        var commonPrefix = 0;
        var maximum = Math.Min(left.Length, right.Length);
        while (commonPrefix < maximum && left[commonPrefix] == right[commonPrefix]) commonPrefix++;
        return commonPrefix >= 4;
    }

    private static string NormalizePromptForComparison(string value) =>
        Whitespace().Replace(EvidenceSeparators().Replace(value.Trim().ToLower(CultureInfo.InvariantCulture), " "), " ").Trim();

    private static bool IsSuperficialRewrite(string candidate, string previous)
    {
        var left = PromptComparisonTokens(candidate);
        var right = PromptComparisonTokens(previous);
        if (left.Count < 3 || right.Count < 3) return false;
        var intersection = left.Intersect(right, StringComparer.Ordinal).Count();
        var union = left.Union(right, StringComparer.Ordinal).Count();
        return union > 0 && intersection / (double)union >= 0.86;
    }

    private static HashSet<string> PromptComparisonTokens(string value)
    {
        var ignored = new HashSet<string>(
            ["acaba", "sizce", "sence", "metne", "göre", "metinde", "soru", "olarak",
             "please", "according", "text", "question"], StringComparer.Ordinal);
        return EvidenceTokens(value).Where(token => !ignored.Contains(token)).ToHashSet(StringComparer.Ordinal);
    }

    private static double EvidenceOverlap(IReadOnlySet<string> expected, IReadOnlySet<string> candidate) =>
        expected.Count == 0 ? 0 : expected.Count(candidate.Contains) / (double)expected.Count;

    private static bool IsMultipleChoice(string type) => Normalize(type) is "multiple_choice" or "multiplechoice" or "çoktan seçmeli";
    private static bool IsShortAnswer(string type) => Normalize(type) is "short_answer" or "shortanswer" or "açık uçlu";

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex("[çğıöşüÇĞİÖŞÜ]")]
    private static partial Regex TurkishCharacters();

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex EvidenceSeparators();

    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceBoundaries();

    [GeneratedRegex(@"^Soru\s+(\d+):", RegexOptions.CultureInvariant)]
    private static partial Regex QuestionNumberPrefix();
}
