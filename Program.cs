using EasyLangApi.Auth;
using EasyLangApi.Data;
using EasyLangApi.Episodes;
using EasyLangApi.Explaining;
using EasyLangApi.Infrastructure;
using EasyLangApi.Lookups;
using EasyLangApi.Quotas;
using EasyLangApi.Recognition;
using EasyLangApi.Review;
using EasyLangApi.Stats;
using EasyLangApi.Vocabulary;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<KnownExceptionHandler>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHealthChecks();

// Строку подключения читаем при создании контекста, а не при старте:
// так её можно подменить в тестах через конфигурацию.
builder.Services.AddDbContext<AppDbContext>((sp, options) =>
    options.UseSqlite(sp.GetRequiredService<IConfiguration>().GetConnectionString("Default")
                      ?? "Data Source=easylang.db"));

builder.Services.AddJwtAuth(builder.Configuration);
builder.Services.AddQuotas(builder.Configuration);
builder.Services.AddPhraseExplainers(builder.Configuration);
builder.Services.Configure<OcrOptions>(builder.Configuration.GetSection(OcrOptions.SectionName));
builder.Services.AddSingleton<ITextRecognizer, TesseractRecognizer>();
builder.Services.AddSingleton<SrtTextSource>();
builder.Services.AddSingleton<ISubtitleSource>(sp => sp.GetRequiredService<SrtTextSource>());
builder.Services.AddSingleton<ISubtitleSource, VttTextSource>();
builder.Services.AddScoped<ImageTextSource>();
builder.Services.AddScoped<EpisodeImporter>();
builder.Services.AddScoped<LookupService>();
builder.Services.AddOptions<ReviewOptions>()
    .Bind(builder.Configuration.GetSection(ReviewOptions.SectionName))
    .Validate(o => o.Adaptive.TargetRetention is > 0 and < 1, "Review:Adaptive:TargetRetention должен быть между 0 и 1.")
    .Validate(o => o.Adaptive.InitialWeights is null || o.Adaptive.InitialWeights.Length == HalfLifeRegression.FeatureNames.Count,
        $"Review:Adaptive:InitialWeights — ровно {HalfLifeRegression.FeatureNames.Count} числа.")
    .Validate(o => o.Adaptive.MinIntervalDays > 0 && o.Adaptive.MaxIntervalDays >= o.Adaptive.MinIntervalDays,
        "Review:Adaptive: нужно 0 < MinIntervalDays <= MaxIntervalDays.")
    .ValidateOnStart();
builder.Services.AddSingleton<FixedIntervalScheduler>();
builder.Services.AddSingleton<AdaptiveScheduler>();
builder.Services.AddSingleton<IReviewScheduler>(sp =>
    sp.GetRequiredService<IOptions<ReviewOptions>>().Value.Scheduler switch
    {
        SchedulerKind.Adaptive => sp.GetRequiredService<AdaptiveScheduler>(),
        _ => sp.GetRequiredService<FixedIntervalScheduler>(),
    });
builder.Services.AddScoped<ExampleLineFinder>();
builder.Services.AddScoped<ReviewService>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
// После аутентификации: лимит считается по пользователю из токена.
app.UseRateLimiter();

await app.Services.MigrateDatabaseAsync();

app.MapHealthChecks("/health").AllowAnonymous();
app.MapGet("/translate", () => "Привет").AllowAnonymous();
app.MapAuthEndpoints();
app.MapEpisodeEndpoints();
app.MapRecognitionEndpoints();
app.MapLookupEndpoints();
app.MapVocabularyEndpoints();
app.MapReviewEndpoints();
app.MapStatsEndpoints();

app.Run();

// Нужен тестам: WebApplicationFactory<Program> ищет этот тип.
public partial class Program
{
}
