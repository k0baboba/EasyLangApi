// Протокол: по строке JSON на запрос в stdin, по строке JSON на ответ в stdout.
//   {"op":"config","target":0.8}                                   → {"ok":true}
//   {"op":"new"}                                                   → {"interval":1}
//   {"op":"review","item":3,"day":10,"elapsed":4,"correct":true}   → {"interval":5,"forgetting":0.39}
//   {"op":"weights"}                                               → {"weights":[...]} — текущие веса модели
// "new" — интервал до первого показа нового элемента (история пуста).
// "review" — ответ на показ: forgetting — прогноз вероятности забыть до ответа, interval — решение после.
// Решения принимает EasyLangApi.Review.AdaptiveScheduler — тот же код, что в приложении:
// ему передаются KnowledgeItem, история элемента (ReviewEvent) и все ответы (UserAnswer).

using System.Text.Json;
using System.Text.Json.Nodes;
using EasyLangApi.Data;
using EasyLangApi.Review;
using Microsoft.Extensions.Options;

var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
AdaptiveScheduler? scheduler = null;
var items = new Dictionary<int, KnowledgeItem>();
var histories = new Dictionary<int, List<ReviewEvent>>();
var answers = new List<UserAnswer>();
var nextEventId = 1;

using var input = new StreamReader(Console.OpenStandardInput());
using var output = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };

while (await input.ReadLineAsync() is { } line)
{
    var request = JsonNode.Parse(line)!.AsObject();
    var op = request["op"]!.GetValue<string>();
    JsonObject response;
    switch (op)
    {
        case "config":
            scheduler = new AdaptiveScheduler(Options.Create(new ReviewOptions
            {
                Scheduler = SchedulerKind.Adaptive,
                Adaptive = new AdaptiveOptions { TargetRetention = request["target"]!.GetValue<double>() },
            }));
            response = new JsonObject { ["ok"] = true };
            break;

        case "new":
        {
            // У нового элемента нет истории: решение зависит только от выученных весов.
            var fresh = new KnowledgeItem { Id = 0, FirstSeenAt = start };
            var schedule = Require(scheduler).Schedule(fresh, [], answers, start);
            response = new JsonObject { ["interval"] = schedule.IntervalDays };
            break;
        }

        case "review":
        {
            var itemId = request["item"]!.GetValue<int>();
            var day = request["day"]!.GetValue<int>();
            var elapsed = request["elapsed"]!.GetValue<int>();
            var correct = request["correct"]!.GetValue<bool>();
            var at = start.AddDays(day);

            if (!items.TryGetValue(itemId, out var item))
            {
                // Первый показ: момент знакомства = день показа минус прошедшие дни.
                item = new KnowledgeItem { Id = itemId + 1, FirstSeenAt = at.AddDays(-elapsed) };
                items[itemId] = item;
                histories[itemId] = [];
            }

            var history = histories[itemId];
            var forgetting = Require(scheduler).ForgettingProbability(item, history, answers, at)!.Value;

            var shown = new ReviewEvent
            {
                Id = nextEventId++, KnowledgeItemId = item.Id, ShownAt = at, AnsweredAt = at, WasCorrect = correct,
            };
            history.Add(shown);
            answers.Add(new UserAnswer(shown.Id, item.Id, item.FirstSeenAt, at, at, correct));

            var schedule = Require(scheduler).Schedule(item, history, answers, at);
            response = new JsonObject { ["interval"] = schedule.IntervalDays, ["forgetting"] = forgetting };
            break;
        }

        case "weights":
            response = new JsonObject
            {
                ["weights"] = new JsonArray(Require(scheduler).Train(answers).Weights.Select(w => (JsonNode)w).ToArray()),
            };
            break;

        default:
            response = new JsonObject { ["error"] = $"unknown op '{op}'" };
            break;
    }

    await output.WriteLineAsync(response.ToJsonString());
}

static AdaptiveScheduler Require(AdaptiveScheduler? scheduler) =>
    scheduler ?? throw new InvalidOperationException("Сначала {\"op\":\"config\"}.");
