using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Exercises.Infrastructure;

/// <summary>
/// Делает гонку воспроизводимой. После <see cref="Arm"/> первые N вызовов SaveChanges ждут друг друга
/// и только потом пишут в базу. Так два параллельных запроса гарантированно успевают прочитать
/// одно и то же состояние, прежде чем один из них запишет своё. Если второй участник не пришёл
/// за <see cref="Wait"/>, первый идёт дальше один: правильное решение может обойтись без SaveChanges.
/// </summary>
public sealed class SaveChangesBarrier : SaveChangesInterceptor
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(3);

    private int _participants;
    private int _arrived;
    private TaskCompletionSource _allArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Arm(int participants)
    {
        _participants = participants;
        _arrived = 0;
        _allArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        var participants = _participants;
        if (participants > 0)
        {
            var number = Interlocked.Increment(ref _arrived);
            if (number <= participants)
            {
                if (number == participants)
                    _allArrived.TrySetResult();
                try
                {
                    await _allArrived.Task.WaitAsync(Wait, cancellationToken);
                }
                catch (TimeoutException)
                {
                    // второй участник не дошёл до SaveChanges — это нормально
                }
            }
        }

        return result;
    }

    /// <summary>Подключает барьер к AppDbContext приложения.</summary>
    public static SaveChangesBarrier AddTo(IServiceCollection services)
    {
        var barrier = new SaveChangesBarrier();
        services.ConfigureDbContext<EasyLangApi.Data.AppDbContext>(options => options.AddInterceptors(barrier));
        return barrier;
    }
}
