using System.Net;
using EasyLangApi.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Exercises.Step14;

/// <summary>
/// Шаг 14. После гонки в базе записан ответ победителя, и интервал элемента посчитан по нему же.
/// Проигравший ничего не поменял ни в ReviewEvent, ни в KnowledgeItem.
/// </summary>
public class DoubleAnswerDataTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Stored_answer_and_schedule_belong_to_the_winner(bool first, bool second)
    {
        var (factory, client, barrier, reviewId) = await Step13.DoubleAnswerTests.ShownCardAsync();
        await using var _ = factory;

        barrier.Arm(participants: 2);
        var responses = await Task.WhenAll(client.AnswerAsync(reviewId, first), client.AnswerAsync(reviewId, second));

        var winner = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        var answer = await winner.ReadJsonAsync();
        var (wasCorrect, intervalDays, answers) = await factory.WithDbAsync(async db =>
        {
            var shown = await db.ReviewEvents.IgnoreQueryFilters().Include(e => e.KnowledgeItem).SingleAsync(e => e.Id == reviewId);
            var count = await db.ReviewEvents.IgnoreQueryFilters().CountAsync(e => e.AnsweredAt != null);
            return (shown.WasCorrect, shown.KnowledgeItem.IntervalDays, count);
        });

        Assert.Equal(1, answers);
        Assert.Equal(answer["wasCorrect"]!.GetValue<bool>(), wasCorrect);
        Assert.Equal(answer["intervalDays"]!.GetValue<double>(), intervalDays);
    }
}
