using ClinicQueue.Api.Clinics;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Api.Data;

/// <summary>
/// Creates a demo clinic with four weeks of synthetic, clearly fake history so the hourly
/// profile and staffing plan have something to show on first run. Uses a fixed random seed,
/// so every fresh database looks the same. Off when Seed:Demo is false.
/// </summary>
public static class DemoSeeder
{
    public const string DemoSlug = "northside";

    // Typical walk-in shape: a morning bump, a lunch peak, and an after-work rush. Patients/hour.
    private static readonly double[] HourlyRate =
    [
        0, 0, 0, 0, 0, 0, 0, 0,
        5, 8, 7, 9, 11, 9, 7, 6, 8, 12, 10, 6,
        0, 0, 0, 0,
    ];

    public static async Task SeedAsync(ClinicDb db, TimeProvider clock, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(clock);
        if (await db.Clinics.AnyAsync(ct))
        {
            return;
        }

        var clinic = new Clinic
        {
            Slug = DemoSlug,
            Name = "Northside Walk-In Clinic (demo)",
            TimeZoneId = "America/Chicago",
            ProvidersOnShift = 2,
            DefaultVisitMinutes = 15,
            FallbackArrivalsPerHour = 6,
        };
        db.Clinics.Add(clinic);
        await db.SaveChangesAsync(ct);

        var random = new Random(20261118);
        var zone = ClinicClock.Zone(clinic);
        var today = ClinicClock.ToLocal(clinic, clock.GetUtcNow().UtcDateTime).Date;
        var ticket = 0;

        for (var daysAgo = 28; daysAgo >= 1; daysAgo--)
        {
            var day = today.AddDays(-daysAgo);
            if (day.DayOfWeek == DayOfWeek.Sunday)
            {
                continue; // closed
            }

            for (var hour = 0; hour < 24; hour++)
            {
                var rate = HourlyRate[hour] * (day.DayOfWeek == DayOfWeek.Monday ? 1.25 : 1.0);
                if (rate == 0)
                {
                    continue;
                }

                // Poisson arrivals: exponential gaps between check-ins.
                var minute = Exponential(random, 60 / rate);
                while (minute < 60)
                {
                    var localCheckIn = DateTime.SpecifyKind(day.AddHours(hour).AddMinutes(minute), DateTimeKind.Unspecified);
                    var checkIn = TimeZoneInfo.ConvertTimeToUtc(localCheckIn, zone);
                    var wait = Exponential(random, 12);
                    var length = Math.Clamp(Exponential(random, 15), 3, 60);
                    var walkedOut = random.NextDouble() < 0.04;
                    db.Visits.Add(new Visit
                    {
                        ClinicId = clinic.Id,
                        TicketCode = $"D-{++ticket:0000}",
                        Initials = "DM",
                        Reason = "demo history",
                        Status = walkedOut ? VisitStatus.LeftWithoutBeingSeen : VisitStatus.Completed,
                        CheckedInAt = checkIn,
                        CalledAt = walkedOut ? null : checkIn.AddMinutes(wait),
                        EndedAt = checkIn.AddMinutes(walkedOut ? wait : wait + length),
                    });
                    minute += Exponential(random, 60 / rate);
                }
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static double Exponential(Random random, double mean) => -mean * Math.Log(1 - random.NextDouble());
}
