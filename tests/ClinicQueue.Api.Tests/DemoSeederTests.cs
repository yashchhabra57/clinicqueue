using System.Net.Http.Json;
using ClinicQueue.Api.Board;
using ClinicQueue.Api.Data;

namespace ClinicQueue.Api.Tests;

public sealed class DemoSeederTests : IDisposable
{
    private readonly ClinicApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Demo_history_produces_a_realistic_day_plan()
    {
        _ = _factory.Server;
        await _factory.WithDbAsync(db => DemoSeeder.SeedAsync(db, _factory.Clock));
        await _factory.WithDbAsync(async db =>
        {
            // Seeding twice is a no-op.
            var before = db.Visits.Count();
            await DemoSeeder.SeedAsync(db, _factory.Clock);
            Assert.Equal(before, db.Visits.Count());
            Assert.InRange(before, 1500, 3500);
        });

        using var staff = _factory.StaffClient();
        var f = await staff.GetFromJsonAsync<ForecastDto>($"/api/clinics/{DemoSeeder.DemoSlug}/forecast");
        Assert.NotNull(f);
        Assert.Equal(0, f.Day[3].ArrivalsPerHour);              // closed overnight
        Assert.True(f.Day[17].ArrivalsPerHour > f.Day[8].ArrivalsPerHour); // after-work rush
        Assert.InRange(f.VisitMinutes, 12, 18);                  // learned from ~15-minute visits
    }
}
