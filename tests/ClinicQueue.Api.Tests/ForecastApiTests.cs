using System.Net;
using System.Net.Http.Json;
using ClinicQueue.Api.Board;
using ClinicQueue.Api.Data;

namespace ClinicQueue.Api.Tests;

public sealed class ForecastApiTests : IAsyncLifetime, IDisposable
{
    private const string Url = $"/api/clinics/{ClinicApiFactory.Slug}/forecast";
    private readonly ClinicApiFactory _factory = new();
    private HttpClient _staff = null!;

    public async Task InitializeAsync()
    {
        // 12/hour fallback with 15-minute visits = 3 Erlangs.
        await _factory.SeedClinicAsync(providers: 3, visitMinutes: 15, fallbackPerHour: 12);
        _staff = _factory.StaffClient();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        _staff.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task Forecast_requires_staff_key()
    {
        var response = await _factory.CreateClient().GetAsync(Url);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Curve_shows_three_providers_is_not_enough_for_twelve_an_hour()
    {
        var f = await _staff.GetFromJsonAsync<ForecastDto>(Url);
        Assert.NotNull(f);
        Assert.Equal(12, f.ArrivalsPerHour, 9);
        Assert.Equal(4, f.RecommendedProviders);

        var three = f.Curve.Single(p => p.Providers == 3);
        Assert.False(three.IsStable); // a = c = 3: the line grows forever
        Assert.Null(three.MeanWaitMinutes);

        var four = f.Curve.Single(p => p.Providers == 4);
        Assert.True(four.MeetsTarget);
        Assert.Equal(7.641509433962266, four.MeanWaitMinutes!.Value, 6);
    }

    [Fact]
    public async Task Stricter_target_can_be_tried_without_saving()
    {
        var f = await _staff.GetFromJsonAsync<ForecastDto>($"{Url}?within=5&level=0.95");
        Assert.True(f!.RecommendedProviders > 4);
        Assert.Equal(0.95, f.Target.ServiceLevel);
    }

    [Theory]
    [InlineData("?level=1.5")]
    [InlineData("?within=-1")]
    public async Task Bad_target_is_400(string query)
    {
        var response = await _staff.GetAsync(Url + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Day_plan_follows_the_clinic_history()
    {
        await _factory.WithDbAsync(async db =>
        {
            var clinicId = db.Clinics.Single().Id;
            // Yesterday: 20 arrivals in the noon hour (18:00 UTC), 2 at 8 AM (14:00 UTC).
            var noon = ClinicApiFactory.Start.UtcDateTime.AddDays(-1).AddHours(3);
            var eight = ClinicApiFactory.Start.UtcDateTime.AddDays(-1).AddHours(-1);
            for (var i = 0; i < 20; i++)
            {
                db.Visits.Add(new Visit { ClinicId = clinicId, TicketCode = $"N{i}", Initials = "NN", Status = VisitStatus.LeftWithoutBeingSeen, CheckedInAt = noon.AddMinutes(i * 3) });
            }

            db.Visits.Add(new Visit { ClinicId = clinicId, TicketCode = "E1", Initials = "EE", Status = VisitStatus.LeftWithoutBeingSeen, CheckedInAt = eight });
            db.Visits.Add(new Visit { ClinicId = clinicId, TicketCode = "E2", Initials = "EE", Status = VisitStatus.LeftWithoutBeingSeen, CheckedInAt = eight.AddMinutes(30) });
            await db.SaveChangesAsync();
        });

        var f = await _staff.GetFromJsonAsync<ForecastDto>(Url);
        Assert.Equal(24, f!.Day.Count);
        Assert.Equal(20, f.Day[12].ArrivalsPerHour);
        Assert.Equal(2, f.Day[8].ArrivalsPerHour);
        Assert.True(f.Day[12].RecommendedProviders > f.Day[8].RecommendedProviders);
    }
}
