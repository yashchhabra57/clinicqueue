namespace ClinicQueue.Core.Estimation;

/// <summary>Blended arrival rate plus the pieces it was built from, so the UI can explain it.</summary>
public sealed record ArrivalRate(double PerHour, double RecentPerHour, double HistoricalPerHour, int RecentArrivals, double WindowHours);

/// <summary>
/// Estimates "how many patients per hour are walking in right now".
///
/// A raw count over the last hour is noisy (3 people in 20 minutes looks like 9/hour), while
/// the historical average for this hour ignores today's flu outbreak. We combine them with a
/// Gamma-Poisson update: treat the historical rate as a prior worth <c>priorWeightHours</c>
/// hours of observation, then add what we actually saw:
///
///   rate = (historical * priorWeight + recentCount) / (priorWeight + windowHours)
///
/// With little recent data the estimate stays near history; as the window fills it follows today.
/// </summary>
public static class ArrivalRateEstimator
{
    public static ArrivalRate Estimate(int recentArrivals, double windowHours, double historicalPerHour, double priorWeightHours)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(recentArrivals);
        ArgumentOutOfRangeException.ThrowIfNegative(windowHours);
        ArgumentOutOfRangeException.ThrowIfNegative(historicalPerHour);
        ArgumentOutOfRangeException.ThrowIfNegative(priorWeightHours);

        var denominator = priorWeightHours + windowHours;
        var blended = denominator == 0
            ? historicalPerHour
            : (historicalPerHour * priorWeightHours + recentArrivals) / denominator;
        var recent = windowHours == 0 ? 0 : recentArrivals / windowHours;
        return new ArrivalRate(blended, recent, historicalPerHour, recentArrivals, windowHours);
    }
}

/// <summary>Average arrivals for each hour of the day, from past check-in times in clinic-local time.</summary>
public static class HourOfDayProfile
{
    /// <param name="localCheckIns">Past check-in times, already converted to the clinic's time zone.</param>
    /// <param name="daysObserved">How many days the history covers (days with zero visits count too).</param>
    /// <param name="fallbackPerHour">Used for every hour when there is no history yet.</param>
    public static double[] Build(IEnumerable<DateTime> localCheckIns, int daysObserved, double fallbackPerHour)
    {
        ArgumentNullException.ThrowIfNull(localCheckIns);
        ArgumentOutOfRangeException.ThrowIfNegative(daysObserved);

        var profile = new double[24];
        if (daysObserved == 0)
        {
            Array.Fill(profile, fallbackPerHour);
            return profile;
        }

        foreach (var t in localCheckIns)
        {
            profile[t.Hour]++;
        }

        for (var h = 0; h < 24; h++)
        {
            profile[h] /= daysObserved;
        }

        return profile;
    }
}
