using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using YAZTEK.Application.Comprehension;
using YAZTEK.Application.Speech;
using YAZTEK.Domain;
using YAZTEK.Infrastructure;
using YAZTEK.Infrastructure.Configuration;
using YAZTEK.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);
var analysisConfiguration = builder.Configuration
    .GetSection(AnalysisOptions.SectionName)
    .Get<AnalysisOptions>() ?? new AnalysisOptions();

builder.Services.AddYaztekInfrastructure(builder.Configuration);
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = analysisConfiguration.MaxUploadBytes;
});
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy
        .WithOrigins(analysisConfiguration.AllowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod());
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("comprehension", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.User.FindFirstValue("sub")
            ?? context.Connection.RemoteIpAddress?.ToString()
            ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 6,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});

if (analysisConfiguration.RequireAuthentication)
{
    var authority = builder.Configuration["Authentication:Authority"];
    var audience = builder.Configuration["Authentication:Audience"];
    if (string.IsNullOrWhiteSpace(authority) || string.IsNullOrWhiteSpace(audience))
    {
        throw new InvalidOperationException("Authentication authority and audience are required when authentication is enabled.");
    }

    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = authority;
            options.Audience = audience;
            options.RequireHttpsMetadata = builder.Configuration.GetValue("Authentication:RequireHttpsMetadata", true);
        });
    builder.Services.AddAuthorization();
}

var app = builder.Build();

app.UseCors();
app.UseRateLimiter();
if (analysisConfiguration.RequireAuthentication)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

app.MapGet("/api/health", async (YaztekDbContext database, CancellationToken cancellationToken) =>
{
    try
    {
        var databaseReady = await database.Database.CanConnectAsync(cancellationToken);
        return Results.Json(new
        {
            status = databaseReady ? "ok" : "degraded",
            application = "YAZTEK",
            database = databaseReady ? "ready" : "unavailable"
        }, statusCode: databaseReady ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
    }
    catch (Exception exception) when (exception is not OperationCanceledException)
    {
        return Results.Json(new
        {
            status = "degraded",
            application = "YAZTEK",
            database = "unavailable",
            detail = "PostgreSQL bağlantısı doğrulanamadı."
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

var comprehensionHealth = app.MapGet("/api/comprehension/health", async (
    IComprehensionAiClient aiClient,
    CancellationToken cancellationToken) =>
{
    var health = await aiClient.GetHealthAsync(cancellationToken);
    return Results.Json(new
    {
        status = health.Status,
        provider = health.Provider,
        model = health.Model,
        structuredOutputs = true,
        detail = health.Detail
    }, statusCode: health.IsReady ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
});

var createComprehensionAttempt = app.MapPost("/api/comprehension/attempts", async (
    HttpContext context,
    [FromBody] CreateComprehensionAttempt request,
    IComprehensionService service,
    CancellationToken cancellationToken) =>
{
    var studentId = ResolveStudentId(context, analysisConfiguration.RequireAuthentication, request.StudentId);
    if (studentId is null) return Results.Unauthorized();
    try
    {
        var result = await service.CreateAttemptAsync(request with { StudentId = studentId }, cancellationToken);
        return result.Status == nameof(ComprehensionAttemptStatus.Generating)
            ? Results.Accepted($"/api/comprehension/attempts/{result.Id}", result)
            : Results.Ok(result);
    }
    catch (ComprehensionServiceException exception)
    {
        return Results.Json(new { error = exception.Code, message = exception.Message }, statusCode: exception.StatusCode);
    }
}).RequireRateLimiting("comprehension");

var submitComprehensionAttempt = app.MapPost("/api/comprehension/attempts/{attemptId:guid}/submit", async (
    Guid attemptId,
    HttpContext context,
    [FromBody] SubmitComprehensionAttempt request,
    IComprehensionService service,
    CancellationToken cancellationToken) =>
{
    var studentId = ResolveStudentId(context, analysisConfiguration.RequireAuthentication, request.StudentId);
    if (studentId is null) return Results.Unauthorized();
    try
    {
        return Results.Ok(await service.SubmitAsync(attemptId, request with { StudentId = studentId }, cancellationToken));
    }
    catch (ComprehensionServiceException exception)
    {
        return Results.Json(new { error = exception.Code, message = exception.Message }, statusCode: exception.StatusCode);
    }
}).RequireRateLimiting("comprehension");

var inferenceHealth = app.MapGet("/api/reading-analysis/health", async (
    ISpeechInferenceClient inference,
    CancellationToken cancellationToken) =>
{
    try
    {
        var response = await inference.GetHealthAsync(cancellationToken);
        return Results.Content(response.Body, "application/json", statusCode: response.StatusCode);
    }
    catch (HttpRequestException exception)
    {
        return Results.Problem(
            title: "Ses çözümleme servisine ulaşılamadı.",
            detail: exception.Message,
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

var analyze = app.MapPost("/api/reading-analysis", async (
    HttpContext context,
    [FromForm] IFormFile audio,
    [FromForm] string referenceText,
    [FromForm] string? textId,
    [FromForm] string? studentId,
    [FromForm] bool consent,
    ISpeechInferenceClient inference,
    IReadingAnalysisStore store,
    IOptions<AnalysisOptions> options,
    CancellationToken cancellationToken) =>
{
    var settings = options.Value;
    if (settings.RequireConsent && !consent)
    {
        return Results.BadRequest(new { error = "Kayıt ve analiz izni gereklidir." });
    }

    if (audio.Length <= 0 || audio.Length > settings.MaxUploadBytes)
    {
        return Results.BadRequest(new { error = $"Ses dosyası boş veya {settings.MaxUploadBytes} bayt sınırını aşıyor." });
    }

    if (string.IsNullOrWhiteSpace(audio.ContentType) ||
        !audio.ContentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new { error = "Yalnızca tarayıcı tarafından üretilen ses dosyaları kabul edilir." });
    }

    referenceText = referenceText.Trim();
    if (referenceText.Length is 0 or > 20_000)
    {
        return Results.BadRequest(new { error = "Referans metin 1 ile 20.000 karakter arasında olmalıdır." });
    }

    var authenticatedStudentId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? context.User.FindFirstValue("sub");
    if (settings.RequireAuthentication && string.IsNullOrWhiteSpace(authenticatedStudentId))
    {
        return Results.Unauthorized();
    }

    var effectiveStudentId = authenticatedStudentId
        ?? (string.IsNullOrWhiteSpace(studentId) ? "local-development-student" : studentId.Trim());
    var effectiveTextId = string.IsNullOrWhiteSpace(textId) ? "unspecified" : textId.Trim();

    SpeechInferenceResult response;
    try
    {
        await using var stream = audio.OpenReadStream();
        response = await inference.AnalyzeAsync(
            stream,
            audio.FileName,
            audio.ContentType,
            referenceText,
            consent,
            cancellationToken);
    }
    catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        return Results.Problem(
            title: "Ses analizi zaman aşımına uğradı.",
            statusCode: StatusCodes.Status504GatewayTimeout);
    }
    catch (HttpRequestException exception)
    {
        return Results.Problem(
            title: "Ses çözümleme servisine ulaşılamadı.",
            detail: exception.Message,
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    if (!response.IsSuccess)
    {
        return Results.Content(response.Body, "application/json", statusCode: response.StatusCode);
    }

    JsonObject payload;
    try
    {
        payload = JsonNode.Parse(response.Body)?.AsObject()
            ?? throw new InvalidOperationException("Inference response was empty.");
    }
    catch (Exception exception) when (exception is not OperationCanceledException)
    {
        return Results.Problem(
            title: "Ses çözümleme servisi geçersiz bir yanıt döndürdü.",
            statusCode: StatusCodes.Status502BadGateway);
    }

    var analysisId = Guid.NewGuid();
    var now = DateTimeOffset.UtcNow;
    payload["analysisId"] = analysisId;
    payload["createdAtUtc"] = now;
    payload["audioRetained"] = false;

    var finalJson = payload.ToJsonString();
    var report = new ReadingAnalysisReport
    {
        Id = analysisId,
        StudentId = effectiveStudentId,
        TextId = effectiveTextId,
        ReferenceTextHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(referenceText))),
        Transcript = payload["transcript"]?.GetValue<string>(),
        ModelId = payload["modelId"]?.GetValue<string>() ?? "unknown",
        AnalysisJson = finalJson,
        ConsentGranted = consent,
        AudioRetained = false,
        CreatedAtUtc = now,
        ExpiresAtUtc = now.AddDays(settings.RetentionDays)
    };
    await store.SaveAsync(report, cancellationToken);

    return Results.Content(finalJson, "application/json", Encoding.UTF8, StatusCodes.Status200OK);
}).DisableAntiforgery();

if (analysisConfiguration.RequireAuthentication)
{
    inferenceHealth.RequireAuthorization();
    analyze.RequireAuthorization();
    comprehensionHealth.RequireAuthorization();
    createComprehensionAttempt.RequireAuthorization();
    submitComprehensionAttempt.RequireAuthorization();
}

app.Run();

static string? ResolveStudentId(HttpContext context, bool authenticationRequired, string? suppliedStudentId)
{
    var authenticated = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? context.User.FindFirstValue("sub");
    if (authenticationRequired) return authenticated;
    return authenticated ?? (string.IsNullOrWhiteSpace(suppliedStudentId) ? "local-development-student" : suppliedStudentId.Trim());
}
