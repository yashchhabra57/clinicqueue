namespace ClinicQueue.Core.Queueing;

/// <summary>A service-level goal such as "80% of patients seen within 30 minutes".</summary>
public sealed record ServiceTarget(double WithinMinutes, double ServiceLevel)
{
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(WithinMinutes);
        if (ServiceLevel is <= 0 or >= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(ServiceLevel), "Service level must be in (0, 1).");
        }
    }
}

public sealed record StaffingOption(int Providers, QueueMetrics Metrics, double ServiceLevel, bool MeetsTarget);

public sealed record HourPlan(int Hour, double ArrivalsPerHour, int? RecommendedProviders, QueueMetrics? Metrics);

public static class StaffingPlanner
{
    public const int DefaultMaxProviders = 40;

    /// <summary>
    /// Fewest providers whose steady-state service level meets the target, or null if
    /// even <paramref name="maxProviders"/> cannot. Service level rises monotonically with c,
    /// so a linear scan from the stability floor is exact and cheap for clinic-sized c.
    /// </summary>
    public static StaffingOption? Recommend(
        double arrivalsPerHour,
        double serviceMinutes,
        ServiceTarget target,
        int maxProviders = DefaultMaxProviders)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.Validate();

        if (arrivalsPerHour == 0)
        {
            // Nobody is arriving; one provider keeps the doors open.
            var idle = ErlangC.Evaluate(1, 0, serviceMinutes);
            return new StaffingOption(1, idle, 1.0, true);
        }

        var offeredLoad = arrivalsPerHour / 60.0 * serviceMinutes;
        // Below ceil(a) (or exactly a) the queue grows without bound.
        var start = Math.Max(1, (int)Math.Floor(offeredLoad) + 1);
        for (var c = start; c <= maxProviders; c++)
        {
            var option = Option(c, arrivalsPerHour, serviceMinutes, target);
            if (option.MeetsTarget)
            {
                return option;
            }
        }

        return null;
    }

    /// <summary>Metrics for each head count in [from, to], used for the "what if" staffing curve.</summary>
    public static IReadOnlyList<StaffingOption> Curve(
        double arrivalsPerHour,
        double serviceMinutes,
        ServiceTarget target,
        int from,
        int to)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.Validate();
        ArgumentOutOfRangeException.ThrowIfNegative(from);
        ArgumentOutOfRangeException.ThrowIfLessThan(to, from);

        var options = new List<StaffingOption>(to - from + 1);
        for (var c = from; c <= to; c++)
        {
            options.Add(Option(c, arrivalsPerHour, serviceMinutes, target));
        }

        return options;
    }

    /// <summary>Hour-by-hour recommendation from an arrival profile (24 values, arrivals per hour).</summary>
    public static IReadOnlyList<HourPlan> PlanDay(
        IReadOnlyList<double> hourlyArrivals,
        double serviceMinutes,
        ServiceTarget target,
        int maxProviders = DefaultMaxProviders)
    {
        ArgumentNullException.ThrowIfNull(hourlyArrivals);
        if (hourlyArrivals.Count != 24)
        {
            throw new ArgumentException("Expected 24 hourly arrival rates.", nameof(hourlyArrivals));
        }

        var plan = new List<HourPlan>(24);
        for (var hour = 0; hour < 24; hour++)
        {
            var lambda = hourlyArrivals[hour];
            var option = Recommend(lambda, serviceMinutes, target, maxProviders);
            plan.Add(new HourPlan(hour, lambda, option?.Providers, option?.Metrics));
        }

        return plan;
    }

    private static StaffingOption Option(int c, double arrivalsPerHour, double serviceMinutes, ServiceTarget target)
    {
        var metrics = ErlangC.Evaluate(c, arrivalsPerHour, serviceMinutes);
        var level = metrics.ServiceLevel(target.WithinMinutes);
        return new StaffingOption(c, metrics, level, metrics.IsStable && level >= target.ServiceLevel);
    }
}
