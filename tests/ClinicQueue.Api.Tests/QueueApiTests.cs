using System.Net;
using System.Net.Http.Json;
using ClinicQueue.Api.Board;
using ClinicQueue.Api.Data;
using ClinicQueue.Api.Visits;

namespace ClinicQueue.Api.Tests;

public sealed class QueueApiTests : IAsyncLifetime, IDisposable
{
    private const string Base = $"/api/clinics/{ClinicApiFactory.Slug}";
    private readonly ClinicApiFactory _factory = new();
    private HttpClient _staff = null!;

    public async Task InitializeAsync()
    {
        await _factory.SeedClinicAsync(providers: 2, visitMinutes: 15);
        _staff = _factory.StaffClient();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        _staff.Dispose();
        _factory.Dispose();
    }

    private async Task<VisitDto> CheckIn(string initials = "JD", string? reason = "sore throat")
    {
        var response = await _staff.PostAsJsonAsync($"{Base}/visits", new CheckInRequest(initials, reason));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadAsync<VisitDto>();
    }

    private Task<DeskBoard> Desk() => _staff.GetFromJsonAsync<DeskBoard>($"{Base}/desk")!;

    [Fact]
    public async Task Health_endpoint_is_up()
    {
        var response = await _factory.CreateClient().GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Staff_endpoints_reject_missing_or_wrong_key()
    {
        var anon = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync($"{Base}/desk")).StatusCode);

        anon.DefaultRequestHeaders.Add("X-Staff-Key", "nope");
        var post = await anon.PostAsJsonAsync($"{Base}/visits", new CheckInRequest("AB", null));
        Assert.Equal(HttpStatusCode.Unauthorized, post.StatusCode);
    }

    [Fact]
    public async Task Check_in_issues_sequential_tickets_that_reset_each_local_day()
    {
        Assert.Equal("A-001", (await CheckIn("ab")).TicketCode);
        var second = await CheckIn("CD");
        Assert.Equal("A-002", second.TicketCode);
        Assert.Equal("CD", second.Initials);

        // 11 PM Chicago is still the same local day...
        _factory.Clock.SetUtcNow(new DateTimeOffset(2026, 11, 19, 5, 0, 0, TimeSpan.Zero));
        Assert.Equal("A-003", (await CheckIn()).TicketCode);

        // ...but 7 AM the next morning starts over.
        _factory.Clock.SetUtcNow(new DateTimeOffset(2026, 11, 19, 13, 0, 0, TimeSpan.Zero));
        Assert.Equal("A-001", (await CheckIn()).TicketCode);
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("John Doe", null)]
    [InlineData("J1", null)]
    [InlineData("JD", "this reason is far too long to fit on the front desk screen and goes on and on and on")]
    public async Task Check_in_validates_input(string initials, string? reason)
    {
        var response = await _staff.PostAsJsonAsync($"{Base}/visits", new CheckInRequest(initials, reason));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_clinic_is_404()
    {
        var response = await _staff.PostAsJsonAsync("/api/clinics/nowhere/visits", new CheckInRequest("AB", null));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _factory.CreateClient().GetAsync("/api/clinics/nowhere/board")).StatusCode);
    }

    [Fact]
    public async Task Call_next_is_first_come_first_served_and_respects_room_capacity()
    {
        var a = await CheckIn("AA");
        _factory.Clock.Advance(TimeSpan.FromMinutes(1));
        var b = await CheckIn("BB");
        _factory.Clock.Advance(TimeSpan.FromMinutes(1));
        await CheckIn("CC");

        var first = await (await _staff.PostAsync($"{Base}/visits/call-next", null)).ReadAsync<VisitDto>();
        var second = await (await _staff.PostAsync($"{Base}/visits/call-next", null)).ReadAsync<VisitDto>();
        Assert.Equal(a.Id, first.Id);
        Assert.Equal(b.Id, second.Id);

        // Two providers, two patients in rooms: the third has to wait.
        var third = await _staff.PostAsync($"{Base}/visits/call-next", null);
        Assert.Equal(HttpStatusCode.Conflict, third.StatusCode);

        await _staff.PostAsync($"{Base}/visits/{a.Id}/complete", null);
        Assert.Equal(HttpStatusCode.OK, (await _staff.PostAsync($"{Base}/visits/call-next", null)).StatusCode);
    }

    [Fact]
    public async Task Call_next_with_empty_queue_is_404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _staff.PostAsync($"{Base}/visits/call-next", null)).StatusCode);
    }

    [Fact]
    public async Task Illegal_transition_is_409()
    {
        var v = await CheckIn();
        var response = await _staff.PostAsync($"{Base}/visits/{v.Id}/complete", null);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Walkout_leaves_the_queue_without_counting_as_a_visit()
    {
        var v = await CheckIn();
        var left = await (await _staff.PostAsync($"{Base}/visits/{v.Id}/leave", null)).ReadAsync<VisitDto>();
        Assert.Equal(nameof(VisitStatus.LeftWithoutBeingSeen), left.Status);
        Assert.Empty((await Desk()).Waiting);
    }

    [Fact]
    public async Task Board_estimates_each_place_in_line()
    {
        // Two providers, both busy, 15-minute visits => a room frees up every 7.5 minutes on average.
        for (var i = 0; i < 5; i++)
        {
            await CheckIn();
            _factory.Clock.Advance(TimeSpan.FromSeconds(30));
        }

        await _staff.PostAsync($"{Base}/visits/call-next", null);
        await _staff.PostAsync($"{Base}/visits/call-next", null);

        var desk = await Desk();
        Assert.Equal(3, desk.Waiting.Count);
        Assert.Equal(2, desk.InRoom.Count);
        Assert.Equal([0, 1, 2], desk.Waiting.Select(w => w.Position));
        Assert.Equal([7.5, 15, 22.5], desk.Waiting.Select(w => w.EstimatedWait!.Mean));
        Assert.All(desk.Waiting, w => Assert.True(w.EstimatedWait!.Low < w.EstimatedWait.Mean && w.EstimatedWait.Mean < w.EstimatedWait.High));
        Assert.Equal(30, desk.WalkInEstimate!.Mean);
    }

    [Fact]
    public async Task Public_board_hides_initials_and_reasons()
    {
        await CheckIn("XYZ", "rash on arm");
        var json = await _factory.CreateClient().GetStringAsync($"{Base}/board");
        Assert.Contains("A-001", json, StringComparison.Ordinal);
        Assert.DoesNotContain("XYZ", json, StringComparison.Ordinal);
        Assert.DoesNotContain("rash", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Adding_a_provider_lets_the_next_patient_in_immediately()
    {
        await CheckIn("AA");
        await CheckIn("BB");
        await CheckIn("CC");
        await _staff.PostAsync($"{Base}/visits/call-next", null);
        await _staff.PostAsync($"{Base}/visits/call-next", null);
        Assert.True((await Desk()).Waiting[0].EstimatedWait!.Mean > 0);

        var put = await _staff.PutAsJsonAsync($"{Base}/staffing", new StaffingRequest(3));
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Equal(0, (await Desk()).Waiting[0].EstimatedWait!.Mean);
    }

    [Fact]
    public async Task Nobody_on_shift_means_no_estimate_and_no_calls()
    {
        await _staff.PutAsJsonAsync($"{Base}/staffing", new StaffingRequest(0));
        await CheckIn();
        var desk = await Desk();
        Assert.Null(desk.Waiting[0].EstimatedWait);
        Assert.False(desk.Model.IsStable);
        Assert.Equal(HttpStatusCode.Conflict, (await _staff.PostAsync($"{Base}/visits/call-next", null)).StatusCode);
    }

    [Fact]
    public async Task Staffing_is_validated()
    {
        var response = await _staff.PutAsJsonAsync($"{Base}/staffing", new StaffingRequest(-1));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Visit_length_estimate_learns_from_completed_visits()
    {
        await _factory.WithDbAsync(async db =>
        {
            var clinicId = db.Clinics.Single().Id;
            var t = ClinicApiFactory.Start.UtcDateTime.AddHours(-3);
            for (var i = 0; i < 30; i++)
            {
                db.Visits.Add(new Visit
                {
                    ClinicId = clinicId,
                    TicketCode = $"Z-{i}",
                    Initials = "ZZ",
                    Status = VisitStatus.Completed,
                    CheckedInAt = t,
                    CalledAt = t,
                    EndedAt = t.AddMinutes(25),
                });
            }

            await db.SaveChangesAsync();
        });

        var model = (await Desk()).Model;
        Assert.Equal(30, model.VisitSamples);
        // Shrunk toward the 15-minute default: (15*10 + 25*30) / 40 = 22.5
        Assert.Equal(22.5, model.VisitMinutes, 9);
    }

    [Fact]
    public async Task Arrival_rate_uses_history_for_this_hour_and_todays_check_ins()
    {
        // Seven past days with 10 check-ins each between 9 and 10 AM local time.
        await _factory.WithDbAsync(async db =>
        {
            var clinicId = db.Clinics.Single().Id;
            for (var day = 1; day <= 7; day++)
            {
                var nineAm = ClinicApiFactory.Start.UtcDateTime.AddDays(-day);
                for (var i = 0; i < 10; i++)
                {
                    db.Visits.Add(new Visit
                    {
                        ClinicId = clinicId,
                        TicketCode = $"H-{day}-{i}",
                        Initials = "HH",
                        Status = VisitStatus.Completed,
                        CheckedInAt = nineAm.AddMinutes(i * 5),
                        CalledAt = nineAm.AddMinutes(i * 5 + 1),
                        EndedAt = nineAm.AddMinutes(i * 5 + 16),
                    });
                }
            }

            await db.SaveChangesAsync();
        });

        var before = (await Desk()).Model;
        Assert.Equal(10, before.HistoricalArrivalsPerHour, 9);
        Assert.Equal(10, before.ArrivalsPerHour, 9); // nothing today yet

        // Busy morning: 12 check-ins in the first 30 minutes.
        for (var i = 0; i < 12; i++)
        {
            await CheckIn();
            _factory.Clock.Advance(TimeSpan.FromMinutes(2.5));
        }

        var after = (await Desk()).Model;
        Assert.Equal(12, after.RecentArrivals);
        // (10*1 + 12) / (1 + 0.5) = 14.67/hour
        Assert.Equal(22.0 / 1.5, after.ArrivalsPerHour, 6);
        Assert.NotNull(after.RecommendedProviders);
    }
}
