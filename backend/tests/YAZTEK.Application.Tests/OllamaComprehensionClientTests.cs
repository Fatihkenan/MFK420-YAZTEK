using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using YAZTEK.Application.Comprehension;
using YAZTEK.Infrastructure.Comprehension;
using YAZTEK.Infrastructure.Configuration;
using Xunit;

namespace YAZTEK.Application.Tests;

public sealed class OllamaComprehensionClientTests
{
    [Fact]
    public async Task GenerateQuestions_RetriesOnlyTruncatedBatch_AndKeepsCompletedBatches()
    {
        var handler = new BatchedQuestionHandler();
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:11434/") };
        var options = Options.Create(new ComprehensionAiOptions
        {
            NumContext = 16_384,
            NumPredict = 1_536,
            QuestionBatchSize = 2,
            MaxOutputRetries = 1
        });
        var client = new OllamaComprehensionClient(httpClient, options);
        var source = "Kırlangıçlar ilkbaharda geri döner. Yuvalarını çatıların altına kurarlar. "
            + "Yavrularını böceklerle beslerler ve sonbaharda sıcak bölgelere göç ederler.";

        var questions = await client.GenerateQuestionsAsync(new GenerationRequest(
            "tr", "Kırlangıçlar", source, [], [], []), CancellationToken.None);

        Assert.Equal(7, questions.Count);
        Assert.Equal(4, questions.Count(question => question.Type == "multiple_choice"));
        Assert.Equal(3, questions.Count(question => question.Type == "short_answer"));
        Assert.Equal(5, handler.RequestBodies.Count); // one truncated call + its retry + three untouched batches
        Assert.All(handler.RequestBodies, body =>
        {
            Assert.False(body["think"]!.GetValue<bool>());
            Assert.Equal(1_536, body["options"]!["num_predict"]!.GetValue<int>());
            Assert.Equal(16_384, body["options"]!["num_ctx"]!.GetValue<int>());
        });
        Assert.Equal([2, 2, 2, 1], handler.RequestBodies.Skip(1).Select(QuestionCount));
        var finalPrompt = handler.RequestBodies[^1]["messages"]![1]!["content"]!.GetValue<string>();
        Assert.Contains("Soru 1", finalPrompt, StringComparison.Ordinal);
        Assert.Contains("Soru 6", finalPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateQuestions_ReportsOllamaLengthDiagnostics_WhenRetryIsDisabled()
    {
        var handler = new BatchedQuestionHandler();
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:11434/") };
        var client = new OllamaComprehensionClient(httpClient, Options.Create(new ComprehensionAiOptions
        {
            NumPredict = 1_536,
            MaxOutputRetries = 0
        }));

        var exception = await Assert.ThrowsAsync<ComprehensionServiceException>(() =>
            client.GenerateQuestionsAsync(new GenerationRequest(
                "tr", "Kırlangıçlar",
                "Kırlangıçlar ilkbaharda geri döner ve yavrularını böceklerle besler. Sonbaharda sıcak bölgelere göç ederler.",
                [], [], []), CancellationToken.None));

        Assert.Equal("ollama_output_truncated", exception.Code);
        Assert.Contains("done_reason=length", exception.Message, StringComparison.Ordinal);
        Assert.Contains("eval_count=1536", exception.Message, StringComparison.Ordinal);
        Assert.Contains("num_predict=1536", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateQuestions_RetriesSmallBatch_WhenModelCopiesAForbiddenQuestion()
    {
        var handler = new DuplicateThenUniqueHandler();
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:11434/") };
        var client = new OllamaComprehensionClient(httpClient, Options.Create(new ComprehensionAiOptions
        {
            QuestionBatchSize = 2,
            MaxOutputRetries = 1
        }));
        const string forbidden = "Elif pusulanın farklı yön göstermesinden ne anladı?";

        var questions = await client.GenerateQuestionsAsync(new GenerationRequest(
            "tr", "Gizemli Pusula",
            "Elif eski bir pusula buldu. Pusulanın ibresi kuzey yerine ormanı gösterdi. Elif ve Kerem işareti izleyerek gizli bir laboratuvar keşfetti.",
            [forbidden], [], [], 1, 0), CancellationToken.None);

        Assert.Equal(2, handler.CallCount);
        Assert.Single(questions);
        Assert.Null(QuestionSetValidator.HistoryConflict(questions[0].Prompt, [forbidden]));
    }

    [Fact]
    public async Task GenerateQuestions_KeepsValidQuestion_AndRegeneratesOnlyDuplicateItem()
    {
        var handler = new DuplicateThenUniqueHandler();
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:11434/") };
        var client = new OllamaComprehensionClient(httpClient, Options.Create(new ComprehensionAiOptions
        {
            QuestionBatchSize = 2,
            MaxOutputRetries = 2
        }));

        var questions = await client.GenerateQuestionsAsync(new GenerationRequest(
            "tr", "Gizemli Pusula",
            "Elif eski bir pusula buldu. Pusulanın ibresi kuzey yerine ormanı gösterdi. Elif ve Kerem işareti izleyerek gizli bir laboratuvar keşfetti.",
            ["Elif pusulanın farklı yön göstermesinden ne anladı?"], [], [], 2, 0), CancellationToken.None);

        Assert.Equal([2, 1], handler.RequestedCounts);
        Assert.Equal(2, questions.Count);
        Assert.Equal(2, questions.Select(question => question.Prompt).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(questions, question => question.Prompt.StartsWith("Pusulanın ormanı göstermesi", StringComparison.Ordinal));
        var retryPrompt = handler.RequestBodies[1]["messages"]![1]!["content"]!.GetValue<string>();
        Assert.Contains("ABSOLUTELY DO NOT RETURN", retryPrompt, StringComparison.Ordinal);
        Assert.Contains("Elif pusulanın farklı yön göstermesinden ne anladı?", retryPrompt, StringComparison.Ordinal);
        Assert.Equal(0.65m, handler.RequestBodies[1]["options"]!["temperature"]!.GetValue<decimal>());
    }

    private static int QuestionCount(JsonObject body)
    {
        var properties = body["format"]!["properties"]!;
        return properties["multipleChoiceQuestions"]!["minItems"]!.GetValue<int>()
            + properties["shortAnswerQuestions"]!["minItems"]!.GetValue<int>();
    }

    private sealed class BatchedQuestionHandler : HttpMessageHandler
    {
        private int _requestNumber;
        private int _questionNumber;
        public List<JsonObject> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var requestBody = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
            RequestBodies.Add(requestBody);
            _requestNumber++;
            if (_requestNumber == 1)
                return Response("{", "length", 1_536, 430);

            var format = requestBody["format"]!;
            var mcCount = format["properties"]!["multipleChoiceQuestions"]!["minItems"]!.GetValue<int>();
            var shortCount = format["properties"]!["shortAnswerQuestions"]!["minItems"]!.GetValue<int>();
            var multipleChoice = new JsonArray();
            var shortAnswer = new JsonArray();
            for (var index = 0; index < mcCount; index++)
                multipleChoice.Add(Question("multiple_choice", ++_questionNumber));
            for (var index = 0; index < shortCount; index++)
                shortAnswer.Add(Question("short_answer", ++_questionNumber));

            var content = new JsonObject
            {
                ["multipleChoiceQuestions"] = multipleChoice,
                ["shortAnswerQuestions"] = shortAnswer
            }.ToJsonString();
            return Response(content, "stop", 210, 430);
        }

        private static JsonObject Question(string type, int number) => new()
        {
            ["type"] = type,
            ["skill"] = number % 2 == 0 ? "key_detail" : "inference",
            ["difficulty"] = "medium",
            ["prompt"] = $"Soru {number}: Metindeki bilgi nasıl açıklanır?",
            ["options"] = type == "multiple_choice"
                ? new JsonArray("Seçenek A", "Seçenek B", "Seçenek C", "Seçenek D")
                : new JsonArray(),
            ["correctOptionIndex"] = type == "multiple_choice" ? 0 : null,
            ["answer"] = "Metindeki doğru ve kısa cevap.",
            ["explanation"] = "Cevap doğrudan metindeki bilgiye dayanır.",
            ["evidence"] = "Kırlangıçlar ilkbaharda geri döner.",
            ["evaluationCriteria"] = new JsonArray("Ana bilgiyi doğru belirtir.")
        };

        private static HttpResponseMessage Response(string content, string doneReason, int evalCount, int promptEvalCount)
        {
            var body = new JsonObject
            {
                ["done"] = true,
                ["done_reason"] = doneReason,
                ["eval_count"] = evalCount,
                ["prompt_eval_count"] = promptEvalCount,
                ["message"] = new JsonObject
                {
                    ["role"] = "assistant",
                    ["thinking"] = null,
                    ["content"] = content
                }
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class DuplicateThenUniqueHandler : HttpMessageHandler
    {
        private const string Forbidden = "Elif pusulanın farklı yön göstermesinden ne anladı?";
        public int CallCount { get; private set; }
        public List<int> RequestedCounts { get; } = [];
        public List<JsonObject> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            var requestBody = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
            RequestBodies.Add(requestBody);
            var requestedCount = requestBody["format"]!["properties"]!["multipleChoiceQuestions"]!["minItems"]!.GetValue<int>();
            RequestedCounts.Add(requestedCount);
            var questions = new JsonArray();
            if (CallCount == 1 && requestedCount == 2)
            {
                questions.Add(Question(
                    "Pusulanın ormanı göstermesi Elif ile Kerem'in hangi eylemine yol açtı?", "cause_effect"));
                questions.Add(Question(Forbidden, "key_detail"));
            }
            else
            {
                questions.Add(Question(CallCount == 1
                    ? Forbidden
                    : "Elif ile Kerem gizli laboratuvara ulaşırken pusuladan nasıl yararlandı?", "inference"));
            }
            var content = new JsonObject
            {
                ["multipleChoiceQuestions"] = questions,
                ["shortAnswerQuestions"] = new JsonArray()
            }.ToJsonString();
            var body = new JsonObject
            {
                ["done"] = true,
                ["done_reason"] = "stop",
                ["eval_count"] = 180,
                ["prompt_eval_count"] = 500,
                ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = content }
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
            };
        }

        private static JsonObject Question(string prompt, string skill) => new()
        {
                ["type"] = "multiple_choice",
                ["skill"] = skill,
                ["difficulty"] = "medium",
                ["prompt"] = prompt,
                ["options"] = new JsonArray("İşareti izlemelerine", "Eve dönmelerine", "Pusulayı atmalarına", "Okula gitmelerine"),
                ["correctOptionIndex"] = 0,
                ["answer"] = "İşareti izlemelerine",
                ["explanation"] = "İki arkadaş pusulanın gösterdiği yönü izledi.",
                ["evidence"] = "Pusulanın ibresi kuzey yerine ormanı gösterdi.",
                ["evaluationCriteria"] = new JsonArray("Neden-sonuç ilişkisini belirtir.")
            };
    }
}
