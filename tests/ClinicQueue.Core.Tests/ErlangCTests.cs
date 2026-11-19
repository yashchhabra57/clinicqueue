using ClinicQueue.Core.Queueing;

namespace ClinicQueue.Core.Tests;

public class ErlangCTests
{
    // Reference values from the closed-form Erlang C formula
    //   C = (a^c / c!) · c/(c-a) / (sum_{k<c} a^k/k! + (a^c / c!) · c/(c-a))
    // computed independently in Python with exact factorials.
    [Theory]
    [InlineData(1, 0.5, 0.5)]
    [InlineData(2, 1.0, 1.0 / 3.0)]
    [InlineData(11, 10.0, 0.6821182046893324)]
    [InlineData(3, 2.4, 0.6471910112359549)]
    [InlineData(5, 4.0, 0.5541125541125541)]
    [InlineData(20, 15.0, 0.16042938741692364)]
    public void ProbabilityOfWaiting_matches_closed_form(int servers, double load, double expected)
    {
        Assert.Equal(expected, ErlangC.ProbabilityOfWaiting(servers, load), 12);
    }

    [Fact]
    public void Single_server_reduces_to_MM1()
    {
        // For M/M/1, P(wait) = rho and Wq = rho / (mu - lambda).
        var m = ErlangC.Evaluate(servers: 1, arrivalsPerHour: 3, serviceMinutes: 10);
        Assert.Equal(0.5, m.Utilization, 12);
        Assert.Equal(0.5, m.ProbabilityOfWaiting, 12);
        Assert.Equal(0.5 / (0.1 - 0.05), m.MeanWaitMinutes, 9);
    }

    [Fact]
    public void Clinic_example_four_providers_twelve_per_hour()
    {
        // 12 arrivals/hour, 15-minute visits => a = 3 Erlangs on 4 providers.
        var m = ErlangC.Evaluate(4, 12, 15);
        Assert.Equal(3.0, m.OfferedLoad, 12);
        Assert.Equal(0.75, m.Utilization, 12);
        Assert.Equal(0.5094339622641509, m.ProbabilityOfWaiting, 12);
        Assert.Equal(7.641509433962266, m.MeanWaitMinutes, 9);
        Assert.Equal(0.9310556104266312, m.ServiceLevel(30), 9);
    }

    [Fact]
    public void Erlang_B_recurrence_is_stable_for_large_server_counts()
    {
        // a^c / c! overflows a double well before c = 500; the recurrence must not.
        var c = ErlangC.ProbabilityOfWaiting(500, 480);
        Assert.InRange(c, 0.0, 1.0);
        Assert.False(double.IsNaN(c));
    }

    [Fact]
    public void Probability_of_waiting_falls_as_providers_are_added()
    {
        var previous = 1.0;
        for (var servers = 4; servers <= 12; servers++)
        {
            var c = ErlangC.ProbabilityOfWaiting(servers, 3.5);
            Assert.True(c < previous, $"C should drop when going to {servers} providers");
            previous = c;
        }
    }

    [Theory]
    [InlineData(2, 2.0)]
    [InlineData(2, 3.0)]
    [InlineData(0, 1.0)]
    public void Overloaded_or_unstaffed_queue_is_unstable(int servers, double load)
    {
        var m = ErlangC.Evaluate(servers, arrivalsPerHour: load * 4, serviceMinutes: 15);
        Assert.False(m.IsStable);
        Assert.Equal(1.0, m.ProbabilityOfWaiting);
        Assert.True(double.IsPositiveInfinity(m.MeanWaitMinutes));
        Assert.True(double.IsPositiveInfinity(m.WaitQuantileMinutes(0.9)));
        Assert.Equal(0.0, m.ServiceLevel(60));
    }

    [Fact]
    public void Empty_clinic_never_waits()
    {
        var m = ErlangC.Evaluate(3, 0, 15);
        Assert.Equal(0.0, m.ProbabilityOfWaiting);
        Assert.Equal(0.0, m.MeanWaitMinutes);
        Assert.Equal(1.0, m.ServiceLevel(0));
    }

    [Fact]
    public void Wait_quantile_inverts_the_tail_probability()
    {
        var m = ErlangC.Evaluate(4, 12, 15);
        var t90 = m.WaitQuantileMinutes(0.9);
        Assert.Equal(0.10, m.ProbabilityWaitExceeds(t90), 9);
        // About 49% of patients never wait, so the 40th percentile wait is zero.
        Assert.Equal(0.0, m.WaitQuantileMinutes(0.4));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(-0.2)]
    public void Wait_quantile_rejects_out_of_range(double q)
    {
        var m = ErlangC.Evaluate(4, 12, 15);
        Assert.Throws<ArgumentOutOfRangeException>(() => m.WaitQuantileMinutes(q));
    }

    [Fact]
    public void Evaluate_rejects_non_positive_service_time()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ErlangC.Evaluate(2, 5, 0));
    }
}
