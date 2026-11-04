using System.Net;
using EasyLangApi.Data;
using EasyLangApi.Review;
using EasyLangApi.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace EasyLangApi.Tests.Api;

public class ReviewSchedulingTests
{
    private const string Srt = """
        1
        00:00:01,000 --> 00:00:02,000
        Come on, spill the beans and break a leg.

        2
        00:00:03,000 --> 00:00:04,000
        Nobody will spill the beans tonight.

        3
        00:00:05,000 --> 00:00:06,000
        Break a leg out there.
        """;

    [Fact]
    public async Task Adaptive_scheduler_is_chosen_by_configuration()
    {
        await using var factory = new ApiFactory().WithSetting("Review:Scheduler", "Adaptive");
        var client = await factory.CreateUserClientAsync();
        var episodeId = await client.UploadSampleAsync();
        await client.ExplainAsync(await client.GetLineIdAsync(episodeId, 2), "beat");

        var card = await (await client.GetAsync("/review/next")).ReadJsonAsync();
        var answer = await (await client.AnswerAsync(card["reviewId"]!.GetValue<int>(), true)).ReadJsonAsync();

        // Прогноз забывания есть только у адаптивного планировщика — значит, выбран он.
        Assert.NotNull(card["forgettingProbability"]);
        // Холодный старт: модель ещё ничему не научилась, интервал минимальный — 1 день.
        Assert.Equal(1, answer["intervalDays"]!.GetValue<double>());
    }

    [Fact]
    public async Task Scheduler_learns_only_from_current_users_answers()
    {
        var spy = new SpyScheduler();
        await using var factory = new ApiFactory
        {
            ConfigureServices = services => services.AddSingleton<IReviewScheduler>(spy),
        }.WithSetting("Jwt:AccessTokenMinutes", "100000"); // тест сдвигает часы на дни
        var alice = await factory.CreateUserClientAsync();
        var bob = await factory.CreateUserClientAsync();

        // Алиса отвечает трижды, Боб — один раз
        await ExplainAndAnswerAsync(alice, "beat", 3, factory);
        await ExplainAndAnswerAsync(bob, "beat", 1, factory);

        Assert.Equal([1, 2, 3, 1], spy.UserAnswerCounts);
        Assert.All(spy.LastAnswers, a => Assert.Equal(spy.LastItemId, a.KnowledgeItemId));
    }

    private static async Task ExplainAndAnswerAsync(HttpClient client, string phrase, int times, ApiFactory factory)
    {
        var episodeId = await client.UploadSampleAsync();
        await client.ExplainAsync(await client.GetLineIdAsync(episodeId, 2), phrase);
        for (var i = 0; i < times; i++)
        {
            var card = await (await client.GetAsync("/review/next")).ReadJsonAsync();
            Assert.Equal(HttpStatusCode.OK, (await client.AnswerAsync(card["reviewId"]!.GetValue<int>(), true)).StatusCode);
            factory.Time.Advance(TimeSpan.FromDays(2));
        }
    }

    /// <summary>Планировщик-шпион: запоминает, сколько ответов пользователя ему передали.</summary>
    private sealed class SpyScheduler : IReviewScheduler
    {
        public List<int> UserAnswerCounts { get; } = [];
        public IReadOnlyList<UserAnswer> LastAnswers { get; private set; } = [];
        public int LastItemId { get; private set; }

        public ReviewSchedule Schedule(
            KnowledgeItem item, IReadOnlyList<ReviewEvent> itemHistory, IReadOnlyList<UserAnswer> userAnswers, DateTime now)
        {
            UserAnswerCounts.Add(userAnswers.Count);
            LastAnswers = userAnswers;
            LastItemId = item.Id;
            return new ReviewSchedule(1, now.AddDays(1), item.Ease);
        }

        public double? ForgettingProbability(
            KnowledgeItem item, IReadOnlyList<ReviewEvent> itemHistory, IReadOnlyList<UserAnswer> userAnswers, DateTime at) => null;
    }

    [Fact]
    public async Task Fixed_scheduler_is_the_default()
    {
        await using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();
        var episodeId = await client.UploadSampleAsync();
        await client.ExplainAsync(await client.GetLineIdAsync(episodeId, 2), "beat");

        var card = await (await client.GetAsync("/review/next")).ReadJsonAsync();
        var answer = await (await client.AnswerAsync(card["reviewId"]!.GetValue<int>(), true)).ReadJsonAsync();

        Assert.Null(card["forgettingProbability"]);
        Assert.Equal(1, answer["intervalDays"]!.GetValue<double>());
    }

    [Fact]
    public async Task Unknown_scheduler_name_stops_the_app_on_start()
    {
        await using var factory = new ApiFactory().WithSetting("Review:Scheduler", "Bogus");

        Assert.ThrowsAny<Exception>(() => factory.CreateClient()); // приложение не стартует
    }

    [Fact]
    public async Task Review_shows_line_where_only_the_reviewed_item_is_unknown()
    {
        await using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();
        var created = await (await client.UploadSrtAsync(Srt, title: "Test")).ReadJsonAsync();
        var episodeId = created["id"]!.GetValue<int>();
        var firstLine = await client.GetLineIdAsync(episodeId, 0);
        // В первой реплике незнакомы обе фразы
        await client.ExplainAsync(firstLine, "spill the beans");
        await client.ExplainAsync(firstLine, "break a leg");

        var card = await (await client.GetAsync("/review/next")).ReadJsonAsync();

        Assert.Equal("spill the beans", card["phrase"]!.GetValue<string>());
        Assert.Equal("Nobody will spill the beans tonight.", card["lineText"]!.GetValue<string>());
        Assert.True(card["singleUnknown"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Without_a_clean_line_the_original_one_is_shown_and_marked()
    {
        await using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();
        var created = await (await client.UploadSrtAsync(
            "1\n00:00:01,000 --> 00:00:02,000\nNo cap, he's beat.\n", title: "Short")).ReadJsonAsync();
        var lineId = await client.GetLineIdAsync(created["id"]!.GetValue<int>(), 0);
        await client.ExplainAsync(lineId, "no cap");
        await client.ExplainAsync(lineId, "beat");

        var card = await (await client.GetAsync("/review/next")).ReadJsonAsync();

        Assert.Equal(lineId, card["lineId"]!.GetValue<int>());
        Assert.False(card["singleUnknown"]!.GetValue<bool>());
        Assert.Equal("beat", Assert.Single(card["otherUnknown"]!.AsArray())!.GetValue<string>());
    }

    [Fact]
    public async Task Examples_endpoint_lists_lines_and_filters_single_unknown()
    {
        await using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();
        var created = await (await client.UploadSrtAsync(Srt, title: "Test")).ReadJsonAsync();
        var firstLine = await client.GetLineIdAsync(created["id"]!.GetValue<int>(), 0);
        var item = await (await client.ExplainAsync(firstLine, "break a leg")).ReadJsonAsync();
        await client.ExplainAsync(firstLine, "spill the beans");
        var itemId = item["knowledgeItemId"]!.GetValue<int>();

        var all = await (await client.GetAsync($"/vocabulary/{itemId}/examples")).ReadJsonArrayAsync();
        var clean = await (await client.GetAsync($"/vocabulary/{itemId}/examples?singleUnknownOnly=true")).ReadJsonArrayAsync();

        Assert.Equal(2, all.Count);
        Assert.Equal("Break a leg out there.", Assert.Single(clean)!["text"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/vocabulary/999/examples")).StatusCode);
    }
}
