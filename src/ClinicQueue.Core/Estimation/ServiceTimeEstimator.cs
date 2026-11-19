namespace ClinicQueue.Core.Estimation;

public sealed record ServiceTime(double MeanMinutes, int SampleSize, int CappedSamples);

/// <summary>
/// Estimates mean visit length from recently completed visits.
///
/// Two guards keep one bad data point from wrecking every estimate on the board:
///  1. Each visit is capped at <c>capMultiple</c> times the configured default, because a
///     provider who forgets to click "complete" produces a 3-hour "visit".
///  2. The mean is shrunk toward the default as if we had already seen <c>priorVisits</c>
///     visits of exactly the default length, so the first two visits of the day do not swing
///     the estimate between 4 and 40 minutes.
/// </summary>
public static class ServiceTimeEstimator
{
    public static ServiceTime Estimate(
        IReadOnlyCollection<double> visitMinutes,
        double defaultMinutes,
        int priorVisits = 10,
        double capMultiple = 4.0)
    {
        ArgumentNullException.ThrowIfNull(visitMinutes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(defaultMinutes);
        ArgumentOutOfRangeException.ThrowIfNegative(priorVisits);
        ArgumentOutOfRangeException.ThrowIfLessThan(capMultiple, 1.0);

        var cap = defaultMinutes * capMultiple;
        var sum = 0.0;
        var capped = 0;
        foreach (var minutes in visitMinutes)
        {
            if (minutes > cap)
            {
                capped++;
                sum += cap;
            }
            else
            {
                sum += Math.Max(0, minutes);
            }
        }

        var n = visitMinutes.Count;
        if (n + priorVisits == 0)
        {
            return new ServiceTime(defaultMinutes, 0, 0);
        }

        var mean = (defaultMinutes * priorVisits + sum) / (priorVisits + n);
        // A zero mean would make the service rate infinite; one minute is a safe floor.
        return new ServiceTime(Math.Max(1.0, mean), n, capped);
    }
}
