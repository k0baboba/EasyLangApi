using System.Net;
using EasyLangApi.Explaining;
using EasyLangApi.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Exercises.Step17;

/// <summary>
/// Шаг 17. Переход на словарь не должен быть молчаливым: запасной разборщик пишет в лог Warning
/// и прикладывает исключение, из-за которого пришлось перейти на словарь.
/// </summary>
public class FallbackLoggingTests
{
    [Fact]
    public async Task Fallback_is_logged_as_warning_with_the_cause()
    {
        await using var factory = Step16.FallbackTests.Factory(HttpStatusCode.BadGateway, services => services.AddFakeLogging());
        var client = await factory.CreateUserClientAsync();
        var lineId = await client.GetLineIdAsync(await client.UploadSampleAsync(), 2);

        var response = await client.ExplainAsync(lineId, "beat");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var logs = factory.Services.GetFakeLogCollector().GetSnapshot();
        Assert.Contains(logs, r => r.Level == LogLevel.Warning && r.Exception is ExplainerUnavailableException);
    }
}
