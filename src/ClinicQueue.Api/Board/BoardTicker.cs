using ClinicQueue.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Api.Board;

/// <summary>
/// Re-pushes every clinic's board on a timer. Nothing in the queue changes between check-ins,
/// but "waited 12 min" and the arrival-rate window do, so lobby screens shouldn't go stale.
/// Board:TickSeconds = 0 turns it off (the tests drive time by hand).
/// </summary>
public sealed class BoardTicker(
    IServiceScopeFactory scopes,
    IBoardNotifier notifier,
    TimeProvider clock,
    IConfiguration config,
    ILogger<BoardTicker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var seconds = config.GetValue("Board:TickSeconds", 30);
        if (seconds <= 0)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(seconds), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            List<string> slugs;
            await using (var scope = scopes.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ClinicDb>();
                slugs = await db.Clinics.AsNoTracking().Select(c => c.Slug).ToListAsync(stoppingToken);
            }

            foreach (var slug in slugs)
            {
                await notifier.ClinicChangedAsync(slug, stoppingToken);
            }

            logger.LogDebug("Refreshed {Count} boards", slugs.Count);
        }
    }
}
