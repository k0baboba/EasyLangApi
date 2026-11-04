using EasyLangApi.Data;
using EasyLangApi.Episodes;
using EasyLangApi.Explaining;
using EasyLangApi.Infrastructure;
using EasyLangApi.Lookups;
using EasyLangApi.Recognition;
using EasyLangApi.Review;
using EasyLangApi.Vocabulary;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<KnownExceptionHandler>();
builder.Services.AddSingleton(TimeProvider.System);

// Строку подключения читаем при создании контекста, а не при старте:
// так её можно подменить в тестах через конфигурацию.
builder.Services.AddDbContext<AppDbContext>((sp, options) =>
    options.UseSqlite(sp.GetRequiredService<IConfiguration>().GetConnectionString("Default")
                      ?? "Data Source=easylang.db"));

builder.Services.AddPhraseExplainers(builder.Configuration);
builder.Services.Configure<OcrOptions>(builder.Configuration.GetSection(OcrOptions.SectionName));
builder.Services.AddSingleton<ITextRecognizer, TesseractRecognizer>();
builder.Services.AddSingleton<SrtTextSource>();
builder.Services.AddScoped<ImageTextSource>();
builder.Services.AddScoped<EpisodeImporter>();
builder.Services.AddScoped<LookupService>();
builder.Services.AddScoped<ReviewService>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

await app.Services.MigrateDatabaseAsync();

app.MapGet("/translate", () => "Привет");
app.MapEpisodeEndpoints();
app.MapRecognitionEndpoints();
app.MapLookupEndpoints();
app.MapVocabularyEndpoints();
app.MapReviewEndpoints();

app.Run();

// Нужен тестам: WebApplicationFactory<Program> ищет этот тип.
public partial class Program
{
}
