using System.Net;
using EasyLangApi.Tests.Infrastructure;
using Exercises.Infrastructure;

namespace Exercises.Step13;

/// <summary>
/// Шаг 13. Два одновременных ответа на один показ. Сейчас оба видят AnsweredAt == null и оба принимаются.
/// Принят должен быть ровно один, второй получает 409 Conflict.
/// </summary>
public class DoubleAnswerTests
{
    internal static async Task<(ApiFactory Factory, HttpClient Client, SaveChangesBarrier Barrier, int ReviewId)> ShownCardAsync()
    {
        SaveChangesBarrier barrier = null!;
        var factory = new ApiFactory { ConfigureServices = services => barrier = SaveChangesBarrier.AddTo(services) };
        var client = await factory.CreateUserClientAsync();
        await Scenario.AddPhrasesAsync(factory, client, "beat");
        var card = await Scenario.GetJsonAsync(client, "/review/next");
        return (factory, client, barrier, card["reviewId"]!.GetValue<int>());
    }

    [Fact]
    public async Task Exactly_one_answer_is_accepted()
    {
        var (factory, client, barrier, reviewId) = await ShownCardAsync();
        await using var _ = factory;

        barrier.Arm(participants: 2);
        var responses = await Task.WhenAll(client.AnswerAsync(reviewId, true), client.AnswerAsync(reviewId, false));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
    }
}
