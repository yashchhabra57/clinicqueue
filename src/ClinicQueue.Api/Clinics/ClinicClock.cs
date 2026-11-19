using ClinicQueue.Api.Data;

namespace ClinicQueue.Api.Clinics;

/// <summary>Converts between UTC (what we store) and the clinic's wall clock (what people see).</summary>
public static class ClinicClock
{
    public static TimeZoneInfo Zone(Clinic clinic)
    {
        ArgumentNullException.ThrowIfNull(clinic);
        return TimeZoneInfo.TryFindSystemTimeZoneById(clinic.TimeZoneId, out var zone) ? zone : TimeZoneInfo.Utc;
    }

    public static DateTime ToLocal(Clinic clinic, DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone(clinic));

    /// <summary>UTC instant of local midnight on the clinic's current day.</summary>
    public static DateTime StartOfLocalDayUtc(Clinic clinic, DateTime utcNow)
    {
        var zone = Zone(clinic);
        var localMidnight = ToLocal(clinic, utcNow).Date;
        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localMidnight, DateTimeKind.Unspecified), zone);
    }
}
