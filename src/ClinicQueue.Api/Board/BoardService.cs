using ClinicQueue.Api.Clinics;
using ClinicQueue.Api.Data;
using ClinicQueue.Core.Estimation;
using ClinicQueue.Core.Queueing;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Api.Board;

public sealed class BoardOptions
{
    /// <summary>How far back "today's arrivals" look.</summary>
    public double RecentWindowMinutes { get; set; } = 60;

    /// <summary>How many hours of observation the historical rate is worth in the blend.</summary>
    public double PriorWeightHours { get; set; } = 1;

    /// <summary>Days of check-ins used for the hour-of-day profile.</summary>
    public int HistoryDays { get; set; } = 28;

    /// <summary>Completed visits used to estimate visit length.</summary>
    public int VisitSamples { get; set; } = 60;
}

/// <summary>
/// Builds a board snapshot: reads the queue from the database, estimates the arrival rate and
/// visit length, then runs the queueing model for the whole room and for each place in line.
/// </summary>
public sealed class BoardService(ClinicDb db, TimeProvider clock, BoardOptions options)
{
    public async Task<DeskBoard?> BuildAsync(string slug, CancellationToken ct = default)
    {
        var clinic = await db.Clinics.AsNoTracking().SingleOrDefaultAsync(c => c.Slug == slug, ct);
        if (clinic is null)
        {
            return null;
        }

        var now = clock.GetUtcNow().UtcDateTime;

        var active = await db.Visits.AsNoTracking()
            .Where(v => v.ClinicId == clinic.Id && (v.Status == VisitStatus.Waiting || v.Status == VisitStatus.InRoom))
            .OrderBy(v => v.CheckedInAt)
            .ToListAsync(ct);
        var waiting = active.Where(v => v.Status == VisitStatus.Waiting).ToList();
        var inRoom = active.Where(v => v.Status == VisitStatus.InRoom).ToList();

        var service = await EstimateServiceTimeAsync(clinic, ct);
        var arrivals = await EstimateArrivalRateAsync(clinic, now, ct);

        var target = new ServiceTarget(clinic.TargetWaitMinutes, clinic.TargetServiceLevel);
        var metrics = ErlangC.Evaluate(clinic.ProvidersOnShift, arrivals.PerHour, service.MeanMinutes);
        var recommendation = StaffingPlanner.Recommend(arrivals.PerHour, service.MeanMinutes, target);

        var queue = waiting.Select((v, position) => new QueueEntryDto(
            v.Id,
            v.TicketCode,
            v.Initials,
            v.Reason,
            position,
            v.CheckedInAt,
            Math.Max(0, (now - v.CheckedInAt).TotalMinutes),
            ToRange(PositionWait.Estimate(position, clinic.ProvidersOnShift, inRoom.Count, service.MeanMinutes))))
            .ToList();

        var rooms = inRoom
            .OrderBy(v => v.CalledAt)
            .Select(v => new RoomDto(v.Id, v.TicketCode, v.Initials, v.Reason, v.CalledAt!.Value, Math.Max(0, (now - v.CalledAt.Value).TotalMinutes)))
            .ToList();

        // Someone walking in now joins the back of the line.
        var walkIn = ToRange(PositionWait.Estimate(waiting.Count, clinic.ProvidersOnShift, inRoom.Count, service.MeanMinutes));

        var model = new ModelDto(
            arrivals.PerHour,
            arrivals.RecentPerHour,
            arrivals.HistoricalPerHour,
            arrivals.RecentArrivals,
            arrivals.WindowHours * 60,
            service.MeanMinutes,
            service.SampleSize,
            Finite(metrics.Utilization),
            metrics.ProbabilityOfWaiting,
            Finite(metrics.MeanWaitMinutes),
            metrics.ServiceLevel(clinic.TargetWaitMinutes),
            metrics.IsStable,
            recommendation?.Providers);

        return new DeskBoard(
            clinic.Slug,
            clinic.Name,
            now,
            clinic.ProvidersOnShift,
            new TargetDto(clinic.TargetWaitMinutes, clinic.TargetServiceLevel),
            model,
            walkIn,
            queue,
            rooms);
    }

    /// <summary>Hour-of-day arrival profile from past days (today excluded so it stays a true prior).</summary>
    public async Task<double[]> HourlyProfileAsync(Clinic clinic, DateTime utcNow, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(clinic);
        var todayStart = ClinicClock.StartOfLocalDayUtc(clinic, utcNow);
        var historyStart = todayStart.AddDays(-options.HistoryDays);

        var checkIns = await db.Visits.AsNoTracking()
            .Where(v => v.ClinicId == clinic.Id && v.CheckedInAt >= historyStart && v.CheckedInAt < todayStart)
            .Select(v => v.CheckedInAt)
            .ToListAsync(ct);

        var local = checkIns.Select(t => ClinicClock.ToLocal(clinic, t)).ToList();
        // Count only days the clinic was open, so closed Sundays don't dilute the averages.
        var openDays = local.Select(t => t.Date).Distinct().Count();
        return HourOfDayProfile.Build(local, openDays, clinic.FallbackArrivalsPerHour);
    }

    private async Task<ServiceTime> EstimateServiceTimeAsync(Clinic clinic, CancellationToken ct)
    {
        var recent = await db.Visits.AsNoTracking()
            .Where(v => v.ClinicId == clinic.Id && v.Status == VisitStatus.Completed && v.CalledAt != null && v.EndedAt != null)
            .OrderByDescending(v => v.EndedAt)
            .Take(options.VisitSamples)
            .Select(v => new { v.CalledAt, v.EndedAt })
            .ToListAsync(ct);

        var minutes = recent.Select(v => (v.EndedAt!.Value - v.CalledAt!.Value).TotalMinutes).ToList();
        return ServiceTimeEstimator.Estimate(minutes, clinic.DefaultVisitMinutes);
    }

    private async Task<ArrivalRate> EstimateArrivalRateAsync(Clinic clinic, DateTime now, CancellationToken ct)
    {
        var profile = await HourlyProfileAsync(clinic, now, ct);
        var historical = profile[ClinicClock.ToLocal(clinic, now).Hour];

        // The window starts at the later of "an hour ago" and the first check-in today. Otherwise
        // a clinic that opened ten minutes ago would average its arrivals over a closed hour.
        var todayStart = ClinicClock.StartOfLocalDayUtc(clinic, now);
        var windowStart = now.AddMinutes(-options.RecentWindowMinutes);
        var firstToday = await db.Visits.AsNoTracking()
            .Where(v => v.ClinicId == clinic.Id && v.CheckedInAt >= todayStart)
            .OrderBy(v => v.CheckedInAt)
            .Select(v => (DateTime?)v.CheckedInAt)
            .FirstOrDefaultAsync(ct);

        if (firstToday is null)
        {
            return ArrivalRateEstimator.Estimate(0, 0, historical, options.PriorWeightHours);
        }

        if (firstToday > windowStart)
        {
            windowStart = firstToday.Value;
        }

        var count = await db.Visits.CountAsync(v => v.ClinicId == clinic.Id && v.CheckedInAt >= windowStart, ct);
        var windowHours = Math.Max(0, (now - windowStart).TotalHours);
        return ArrivalRateEstimator.Estimate(count, windowHours, historical, options.PriorWeightHours);
    }

    private static RangeDto? ToRange(WaitEstimate? e) =>
        e is null ? null : new RangeDto(Math.Round(e.MeanMinutes, 1), Math.Round(e.LowMinutes, 1), Math.Round(e.HighMinutes, 1));

    private static double? Finite(double value) => double.IsFinite(value) ? value : null;
}
