using ClinicQueue.Core.Queueing;

namespace ClinicQueue.Core.Tests;

public class PositionWaitTests
{
    [Fact]
    public void Shape_one_is_the_exponential_distribution()
    {
        // Erlang(1, r) is Exponential(r): F(t) = 1 - e^{-rt}, median = ln 2 / r.
        Assert.Equal(1 - Math.Exp(-2.0), ErlangDistribution.Cdf(1, 0.2, 10), 12);
        Assert.Equal(Math.Log(2) / 0.2, ErlangDistribution.Quantile(1, 0.2, 0.5), 7);
    }

    // Reference values computed in Python with exact factorials and 200-step bisection.
    [Theory]
    [InlineData(1, 0.2, 0.5268025782891317, 11.512925464970225, 0.8646647167633873)]
    [InlineData(3, 4.0 / 15.0, 4.132744980934955, 19.958701266878283, 0.49817507450198883)]
    [InlineData(10, 0.5, 12.442609210450064, 28.41198058430563, 0.03182805730620475)]
    public void Erlang_quantiles_match_reference(int n, double rate, double p10, double p90, double cdfAt10)
    {
        Assert.Equal(p10, ErlangDistribution.Quantile(n, rate, 0.1), 6);
        Assert.Equal(p90, ErlangDistribution.Quantile(n, rate, 0.9), 6);
        Assert.Equal(cdfAt10, ErlangDistribution.Cdf(n, rate, 10), 10);
    }

    [Fact]
    public void Cdf_does_not_underflow_for_long_lines()
    {
        // e^{-x} underflows to 0 for x > ~745; log-space summation keeps this accurate.
        var median = ErlangDistribution.Quantile(800, 1.0, 0.5);
        Assert.InRange(median, 795, 805);
    }

    [Fact]
    public void Patient_is_called_now_when_a_room_is_free()
    {
        // 3 providers, 1 busy: the next two patients go straight in.
        Assert.Equal(WaitEstimate.Now, PositionWait.Estimate(0, 3, 1, 15));
        Assert.Equal(WaitEstimate.Now, PositionWait.Estimate(1, 3, 1, 15));
        Assert.NotEqual(WaitEstimate.Now, PositionWait.Estimate(2, 3, 1, 15));
    }

    [Fact]
    public void Wait_scales_with_place_in_line()
    {
        // 4 providers all busy, 15-minute visits: one room frees up every 15/4 = 3.75 minutes.
        var third = PositionWait.Estimate(2, 4, 4, 15)!;
        Assert.Equal(3, third.CompletionsAhead);
        Assert.Equal(3 * 3.75, third.MeanMinutes, 9);
        Assert.Equal(4.132744980934955, third.LowMinutes, 6);
        Assert.Equal(19.958701266878283, third.HighMinutes, 6);
        Assert.True(third.LowMinutes < third.MeanMinutes && third.MeanMinutes < third.HighMinutes);
    }

    [Fact]
    public void Adding_a_provider_shortens_the_wait()
    {
        var before = PositionWait.Estimate(5, 2, 2, 20)!;
        var after = PositionWait.Estimate(5, 3, 2, 20)!;
        Assert.True(after.MeanMinutes < before.MeanMinutes);
    }

    [Fact]
    public void Shrinking_shift_counts_the_overflow_visits()
    {
        // Shift dropped to 2 providers while 3 patients are still in rooms: the next patient
        // needs two visits to end (one to get back to 2 busy, one to free a room).
        var next = PositionWait.Estimate(0, 2, 3, 10)!;
        Assert.Equal(2, next.CompletionsAhead);
    }

    [Fact]
    public void Nobody_on_shift_means_no_estimate()
    {
        Assert.Null(PositionWait.Estimate(0, 0, 0, 15));
    }
}
