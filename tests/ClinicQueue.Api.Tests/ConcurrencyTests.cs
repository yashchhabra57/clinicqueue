using ClinicQueue.Api.Data;
using ClinicQueue.Api.Visits;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicQueue.Api.Tests;

/// <summary>
/// Two front-desk screens act on stale copies of the same rows. The concurrency tokens must make
/// the second save fail instead of silently overwriting the first (or over-filling rooms).
/// </summary>
public sealed class ConcurrencyTests : IAsyncLifetime, IDisposable
{
    private readonly ClinicApiFactory _factory = new();

    public Task InitializeAsync() => _factory.SeedClinicAsync(providers: 1);

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Second_writer_on_the_same_visit_loses()
    {
        Guid id = default;
        await _factory.WithDbAsync(async db =>
        {
            var visit = new Visit { ClinicId = db.Clinics.Single().Id, TicketCode = "A-001", Initials = "AB", CheckedInAt = ClinicApiFactory.Start.UtcDateTime };
            db.Visits.Add(visit);
            await db.SaveChangesAsync();
            id = visit.Id;
        });

        await using var scopeA = _factory.Services.CreateAsyncScope();
        await using var scopeB = _factory.Services.CreateAsyncScope();
        var dbA = scopeA.ServiceProvider.GetRequiredService<ClinicDb>();
        var dbB = scopeB.ServiceProvider.GetRequiredService<ClinicDb>();
        var a = await dbA.Visits.SingleAsync(v => v.Id == id);
        var b = await dbB.Visits.SingleAsync(v => v.Id == id);

        // Desk A calls the patient; desk B, looking at the same stale row, marks them as a walkout.
        Assert.True(VisitWorkflow.TryApply(a, VisitAction.Call, ClinicApiFactory.Start.UtcDateTime));
        Assert.True(VisitWorkflow.TryApply(b, VisitAction.Leave, ClinicApiFactory.Start.UtcDateTime));
        await dbA.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => dbB.SaveChangesAsync());

        await _factory.WithDbAsync(async db =>
            Assert.Equal(VisitStatus.InRoom, (await db.Visits.SingleAsync(v => v.Id == id)).Status));
    }

    [Fact]
    public async Task Two_calls_for_the_last_free_room_cannot_both_succeed()
    {
        await using var scopeA = _factory.Services.CreateAsyncScope();
        await using var scopeB = _factory.Services.CreateAsyncScope();
        var dbA = scopeA.ServiceProvider.GetRequiredService<ClinicDb>();
        var dbB = scopeB.ServiceProvider.GetRequiredService<ClinicDb>();

        // Both desks read the clinic while its single room is free...
        var clinicA = await dbA.Clinics.SingleAsync();
        var clinicB = await dbB.Clinics.SingleAsync();

        // ...and each bumps the clinic version as part of calling a different patient.
        clinicA.Version = Guid.NewGuid();
        clinicB.Version = Guid.NewGuid();
        await dbA.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => dbB.SaveChangesAsync());
    }
}
