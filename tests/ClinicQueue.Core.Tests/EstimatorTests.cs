using ClinicQueue.Core.Estimation;

namespace ClinicQueue.Core.Tests;

public class EstimatorTests
{
    [Fact]
    public void Arrival_rate_with_no_recent_window_is_the_historical_rate()
    {
        var r = ArrivalRateEstimator.Estimate(0, 0, 8, 1);
        Assert.Equal(8, r.PerHour);
    }

    [Fact]
    public void Arrival_rate_blends_history_with_today()
    {
        // History says 6/hour (worth 1 hour of evidence). Today: 6 arrivals in the last 30 minutes.
        // (6*1 + 6) / (1 + 0.5) = 8/hour: pulled up from 6 but not all the way to the raw 12.
        var r = ArrivalRateEstimator.Estimate(6, 0.5, 6, 1);
        Assert.Equal(8, r.PerHour, 12);
        Assert.Equal(12, r.RecentPerHour, 12);
    }

    [Fact]
    public void More_recent_data_moves_the_estimate_closer_to_today()
    {
        var shortWindow = ArrivalRateEstimator.Estimate(10, 1, 4, 2).PerHour; // raw 10/h
        var longWindow = ArrivalRateEstimator.Estimate(30, 3, 4, 2).PerHour;  // raw 10/h
        Assert.True(Math.Abs(longWindow - 10) < Math.Abs(shortWindow - 10));
    }

    [Fact]
    public void Hour_profile_averages_over_days_observed()
    {
        var day = new DateTime(2026, 11, 2);
        var checkIns = new[]
        {
            day.AddHours(9.1), day.AddHours(9.5), day.AddHours(13.2),
            day.AddDays(1).AddHours(9.9),
        };
        var profile = HourOfDayProfile.Build(checkIns, daysObserved: 2, fallbackPerHour: 5);
        Assert.Equal(1.5, profile[9]);
        Assert.Equal(0.5, profile[13]);
        Assert.Equal(0, profile[3]);
    }

    [Fact]
    public void Hour_profile_without_history_uses_the_fallback()
    {
        var profile = HourOfDayProfile.Build([], 0, 5);
        Assert.All(profile, v => Assert.Equal(5, v));
    }

    [Fact]
    public void Service_time_with_no_visits_is_the_default()
    {
        Assert.Equal(15, ServiceTimeEstimator.Estimate([], 15).MeanMinutes);
    }

    [Fact]
    public void Service_time_shrinks_early_samples_toward_the_default()
    {
        // Two quick 5-minute visits should not convince us visits take 5 minutes.
        // (15*10 + 5 + 5) / 12 = 13.33
        var s = ServiceTimeEstimator.Estimate([5.0, 5.0], 15);
        Assert.Equal(160.0 / 12.0, s.MeanMinutes, 9);
    }

    [Fact]
    public void Forgotten_completions_are_capped()
    {
        // A 180-minute "visit" counts as 4 x 15 = 60 minutes.
        var s = ServiceTimeEstimator.Estimate([180.0], 15, priorVisits: 0);
        Assert.Equal(60, s.MeanMinutes);
        Assert.Equal(1, s.CappedSamples);
    }

    [Fact]
    public void Many_samples_dominate_the_prior()
    {
        var visits = Enumerable.Repeat(20.0, 200).ToArray();
        var s = ServiceTimeEstimator.Estimate(visits, 15);
        Assert.InRange(s.MeanMinutes, 19.7, 20.0);
        Assert.Equal(200, s.SampleSize);
    }
}
