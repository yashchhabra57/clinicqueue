namespace ClinicQueue.Core.Queueing;

/// <summary>Estimated wait for one patient, with a likely range (10th to 90th percentile).</summary>
public sealed record WaitEstimate(double MeanMinutes, double LowMinutes, double HighMinutes, int CompletionsAhead)
{
    public static readonly WaitEstimate Now = new(0, 0, 0, 0);
}

/// <summary>
/// Wait estimate for a specific place in line, conditioned on what is happening right now
/// (who is in a room, how many providers are on shift) instead of the long-run average.
///
/// Model: when every provider is busy, visits end as a Poisson process with rate c / S
/// (c providers, mean visit S minutes, exponential so elapsed time doesn't matter). A patient
/// with k people ahead of them and f free rooms needs n = k - f + 1 visits to end before they
/// are called. The time for n events of a Poisson process with rate r is Erlang(n, r) (a Gamma
/// distribution with integer shape), so the mean is n / r and the range comes from its quantiles.
/// </summary>
public static class PositionWait
{
    public const double LowQuantile = 0.10;
    public const double HighQuantile = 0.90;

    /// <param name="positionInLine">0 for the next patient to be called.</param>
    /// <param name="providersOnShift">c. Returns null when nobody is on shift (the wait is unknowable).</param>
    /// <param name="patientsInService">Patients currently in a room.</param>
    /// <param name="serviceMinutes">Mean visit length S.</param>
    public static WaitEstimate? Estimate(int positionInLine, int providersOnShift, int patientsInService, double serviceMinutes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(positionInLine);
        ArgumentOutOfRangeException.ThrowIfNegative(providersOnShift);
        ArgumentOutOfRangeException.ThrowIfNegative(patientsInService);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(serviceMinutes);

        if (providersOnShift == 0)
        {
            return null;
        }

        // Negative when the shift shrank while patients were still in rooms: those extra
        // visits must finish before anyone new is called.
        var freeRooms = providersOnShift - patientsInService;
        var completionsNeeded = positionInLine - freeRooms + 1;
        if (completionsNeeded <= 0)
        {
            return WaitEstimate.Now;
        }

        var ratePerMinute = providersOnShift / serviceMinutes;
        var mean = completionsNeeded / ratePerMinute;
        var low = ErlangDistribution.Quantile(completionsNeeded, ratePerMinute, LowQuantile);
        var high = ErlangDistribution.Quantile(completionsNeeded, ratePerMinute, HighQuantile);
        return new WaitEstimate(mean, low, high, completionsNeeded);
    }
}

/// <summary>Erlang(n, rate): the waiting time until the n-th event of a Poisson process.</summary>
public static class ErlangDistribution
{
    /// <summary>
    /// CDF: F(t) = 1 - sum_{i=0}^{n-1} e^{-x} x^i / i!, with x = rate·t.
    /// The sum is accumulated in log space so large n and x don't underflow e^{-x}.
    /// </summary>
    public static double Cdf(int shape, double rate, double t)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(shape);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rate);
        if (t <= 0)
        {
            return 0.0;
        }

        var x = rate * t;
        var logX = Math.Log(x);
        var logTerm = -x;           // log(e^{-x} x^0 / 0!)
        var logSum = logTerm;
        for (var i = 1; i < shape; i++)
        {
            logTerm += logX - Math.Log(i);
            logSum = LogAdd(logSum, logTerm);
        }

        var survival = Math.Exp(logSum);
        return Math.Clamp(1.0 - survival, 0.0, 1.0);
    }

    /// <summary>Inverse CDF by bisection. The CDF is continuous and increasing, so this always converges.</summary>
    public static double Quantile(int shape, double rate, double p)
    {
        if (p is <= 0 or >= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(p), "p must be in (0, 1).");
        }

        var mean = shape / rate;
        var sd = Math.Sqrt(shape) / rate;
        double lo = 0, hi = mean + 12 * sd;
        while (Cdf(shape, rate, hi) < p)
        {
            hi *= 2;
        }

        for (var iter = 0; iter < 100 && hi - lo > 1e-9 * Math.Max(1, hi); iter++)
        {
            var mid = (lo + hi) / 2;
            if (Cdf(shape, rate, mid) < p)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }

        return (lo + hi) / 2;
    }

    private static double LogAdd(double a, double b)
    {
        var max = Math.Max(a, b);
        return max + Math.Log(Math.Exp(a - max) + Math.Exp(b - max));
    }
}
