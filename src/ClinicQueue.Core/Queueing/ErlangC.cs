namespace ClinicQueue.Core.Queueing;

/// <summary>
/// Steady-state results for an M/M/c queue: Poisson arrivals, exponential
/// service times, c identical servers (providers), first-come first-served.
/// All times are in minutes.
/// </summary>
/// <param name="Servers">Number of providers seeing patients (c).</param>
/// <param name="ArrivalsPerHour">Arrival rate (lambda), patients per hour.</param>
/// <param name="ServiceMinutes">Mean visit length (1 / mu), minutes.</param>
/// <param name="OfferedLoad">a = lambda / mu, in Erlangs (how many providers the demand "needs").</param>
/// <param name="Utilization">rho = a / c. Must be below 1 for the queue to be stable.</param>
/// <param name="ProbabilityOfWaiting">Erlang C: chance an arriving patient has to wait at all.</param>
/// <param name="MeanWaitMinutes">Expected time in the waiting room (Wq). Infinity when unstable.</param>
public sealed record QueueMetrics(
    int Servers,
    double ArrivalsPerHour,
    double ServiceMinutes,
    double OfferedLoad,
    double Utilization,
    double ProbabilityOfWaiting,
    double MeanWaitMinutes)
{
    public bool IsStable => Utilization < 1.0;

    /// <summary>Rate (per minute) at which the queue drains when every provider is busy, minus arrivals.</summary>
    public double DrainRatePerMinute => Servers / ServiceMinutes - ArrivalsPerHour / 60.0;

    /// <summary>P(wait &gt; t). In M/M/c the conditional wait of a queued patient is exponential.</summary>
    public double ProbabilityWaitExceeds(double minutes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minutes);
        if (!IsStable)
        {
            return 1.0;
        }

        return ProbabilityOfWaiting * Math.Exp(-DrainRatePerMinute * minutes);
    }

    /// <summary>Service level: share of patients seen within <paramref name="minutes"/>.</summary>
    public double ServiceLevel(double minutes) => 1.0 - ProbabilityWaitExceeds(minutes);

    /// <summary>The wait that a fraction <paramref name="quantile"/> of patients stay under.</summary>
    public double WaitQuantileMinutes(double quantile)
    {
        if (quantile is <= 0 or >= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(quantile), "Quantile must be in (0, 1).");
        }

        if (!IsStable)
        {
            return double.PositiveInfinity;
        }

        var tail = 1.0 - quantile;
        // P(W > t) = C * e^(-r t) = tail  =>  t = ln(C / tail) / r, and 0 if C <= tail.
        return ProbabilityOfWaiting <= tail ? 0.0 : Math.Log(ProbabilityOfWaiting / tail) / DrainRatePerMinute;
    }
}

public static class ErlangC
{
    /// <summary>
    /// Erlang B blocking probability via the stable recurrence
    /// B(0) = 1, B(k) = a·B(k-1) / (k + a·B(k-1)). This avoids computing a^c / c!,
    /// which overflows a double past roughly c = 170.
    /// </summary>
    public static double ErlangB(int servers, double offeredLoad)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(servers);
        ArgumentOutOfRangeException.ThrowIfNegative(offeredLoad);

        var b = 1.0;
        for (var k = 1; k <= servers; k++)
        {
            b = offeredLoad * b / (k + offeredLoad * b);
        }

        return b;
    }

    /// <summary>
    /// Erlang C (probability of waiting) from Erlang B:
    /// C = B / (1 - rho·(1 - B)). Returns 1 when the system is unstable (rho &gt;= 1).
    /// </summary>
    public static double ProbabilityOfWaiting(int servers, double offeredLoad)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offeredLoad);
        if (servers <= 0)
        {
            return 1.0;
        }

        if (offeredLoad == 0)
        {
            return 0.0;
        }

        var rho = offeredLoad / servers;
        if (rho >= 1.0)
        {
            return 1.0;
        }

        var b = ErlangB(servers, offeredLoad);
        return b / (1.0 - rho * (1.0 - b));
    }

    public static QueueMetrics Evaluate(int servers, double arrivalsPerHour, double serviceMinutes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(servers);
        ArgumentOutOfRangeException.ThrowIfNegative(arrivalsPerHour);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(serviceMinutes);

        var lambdaPerMinute = arrivalsPerHour / 60.0;
        var offeredLoad = lambdaPerMinute * serviceMinutes;
        // With nobody on shift, anyone who walks in waits indefinitely.
        var utilization = servers == 0 ? double.PositiveInfinity : offeredLoad / servers;
        var pWait = ProbabilityOfWaiting(servers, offeredLoad);

        double meanWait;
        if (utilization >= 1.0)
        {
            meanWait = double.PositiveInfinity;
        }
        else
        {
            // Wq = C / (c·mu - lambda)
            meanWait = pWait / (servers / serviceMinutes - lambdaPerMinute);
        }

        return new QueueMetrics(servers, arrivalsPerHour, serviceMinutes, offeredLoad, utilization, pWait, meanWait);
    }
}
