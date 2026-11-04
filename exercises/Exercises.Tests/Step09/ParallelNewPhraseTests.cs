using System.Net;
using EasyLangApi.Tests.Infrastructure;
using Exercises.Infrastructure;

namespace Exercises.Step09;

/// <summary>
/// Шаг 09. Два одновременных разбора одной новой фразы. Оба не находят элемент словаря, оба создают его,
/// и второй SaveChanges падает на уникальном индексе (UserId, PhraseNormalized). Сейчас клиент получает 500.
/// На этом шаге достаточно, чтобы проигравший получил 409 Conflict: это ожидаемая ситуация, а не баг.
/// (На шаге 10 он будет получать 200 — этот тест тоже это допускает.)
/// SaveChangesBarrier делает эту гонку воспроизводимой: оба запроса доходят до записи, прежде чем кто-то запишет.
/// </summary>
public class ParallelNewPhraseTests
{
    [Fact]
    public async Task Loser_gets_409_not_500()
    {
        SaveChangesBarrier barrier = null!;
        await using var factory = new ApiFactory { ConfigureServices = services => barrier = SaveChangesBarrier.AddTo(services) };
        var client = await factory.CreateUserClientAsync();
        var episodeId = await client.UploadSampleAsync();
        var lineId = await client.GetLineIdAsync(episodeId, 2);

        barrier.Arm(participants: 2);
        var responses = await Task.WhenAll(client.ExplainAsync(lineId, "beat"), client.ExplainAsync(lineId, "Beat!"));

        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.All(responses, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
        foreach (var response in responses.Where(r => r.StatusCode == HttpStatusCode.Conflict))
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
