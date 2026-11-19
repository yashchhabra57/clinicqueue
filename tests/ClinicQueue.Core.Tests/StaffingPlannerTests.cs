using ClinicQueue.Core.Queueing;

namespace ClinicQueue.Core.Tests;

public class StaffingPlannerTests
{
    private static readonly ServiceTarget EightyInThirty = new(30, 0.80);

    [Fact]
    public void Recommends_the_fewest_providers_that_meet_the_target()
    {
        // 12/hour with 15-minute visits needs 3 Erlangs; 3 providers is unstable,
        // 4 providers gives 93% seen within 30 minutes.
        var option = StaffingPlanner.Recommend(12, 15, EightyInThirty);
        Assert.NotNull(option);
        Assert.Equal(4, option.Providers);
        Assert.True(option.MeetsTarget);

        var oneFewer = ErlangC.Evaluate(option.Providers - 1, 12, 15);
        Assert.False(oneFewer.IsStable && oneFewer.ServiceLevel(30) >= 0.8);
    }

    [Fact]
    public void Stricter_target_never_needs_fewer_providers()
    {
        var loose = StaffingPlanner.Recommend(20, 18, new ServiceTarget(30, 0.7))!;
        var strict = StaffingPlanner.Recommend(20, 18, new ServiceTarget(10, 0.95))!;
        Assert.True(strict.Providers >= loose.Providers);
    }

    [Fact]
    public void Returns_null_when_demand_exceeds_the_cap()
    {
        Assert.Null(StaffingPlanner.Recommend(600, 20, EightyInThirty, maxProviders: 10));
    }

    [Fact]
    public void No_arrivals_still_staffs_one_provider()
    {
        var option = StaffingPlanner.Recommend(0, 15, EightyInThirty)!;
        Assert.Equal(1, option.Providers);
    }

    [Fact]
    public void Curve_reports_each_head_count_and_flags_unstable_ones()
    {
        var curve = StaffingPlanner.Curve(12, 15, EightyInThirty, 2, 6);
        Assert.Equal([2, 3, 4, 5, 6], curve.Select(o => o.Providers));
        Assert.False(curve[0].Metrics.IsStable);
        Assert.False(curve[1].Metrics.IsStable); // exactly a = c is still unstable
        Assert.True(curve[2].MeetsTarget);
        // Service level rises with every extra provider.
        for (var i = 3; i < curve.Count; i++)
        {
            Assert.True(curve[i].ServiceLevel > curve[i - 1].ServiceLevel);
        }
    }

    [Fact]
    public void Day_plan_follows_the_arrival_profile()
    {
        var profile = new double[24];
        profile[9] = 6;   // morning
        profile[12] = 18; // lunch rush
        profile[17] = 10;

        var plan = StaffingPlanner.PlanDay(profile, 15, EightyInThirty);
        Assert.Equal(24, plan.Count);
        Assert.Equal(1, plan[3].RecommendedProviders); // closed hours keep the floor of 1
        Assert.True(plan[12].RecommendedProviders > plan[9].RecommendedProviders);
        Assert.True(plan[12].RecommendedProviders >= plan[17].RecommendedProviders);
    }

    [Fact]
    public void Day_plan_requires_24_hours()
    {
        Assert.Throws<ArgumentException>(() => StaffingPlanner.PlanDay(new double[23], 15, EightyInThirty));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    public void Invalid_service_level_is_rejected(double level)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => StaffingPlanner.Recommend(5, 15, new ServiceTarget(30, level)));
    }
}
