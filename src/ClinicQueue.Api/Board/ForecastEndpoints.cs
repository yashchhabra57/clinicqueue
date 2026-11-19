using ClinicQueue.Api.Auth;
using ClinicQueue.Api.Clinics;
using ClinicQueue.Api.Data;
using ClinicQueue.Api.Visits;
using ClinicQueue.Core.Queueing;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Api.Board;

public sealed record CurvePointDto(int Providers, bool IsStable, double ServiceLevel, double ProbabilityOfWaiting, double? MeanWaitMinutes, bool MeetsTarget);

public sealed record HourDto(int Hour, double ArrivalsPerHour, int? RecommendedProviders);

public sealed record ForecastDto(
    string Slug,
    TargetDto Target,
    double ArrivalsPerHour,
    double VisitMinutes,
    int ProvidersOnShift,
    int? RecommendedProviders,
    IReadOnlyList<CurvePointDto> Curve,
    IReadOnlyList<HourDto> Day);

public static class ForecastEndpoints
{
    /// <summary>
    /// "What if" staffing: the service level for each head count right now, and an hour-by-hour
    /// plan for a typical day from the clinic's own history. Optional query overrides let a
    /// manager try a different target (e.g. ?within=20&amp;level=0.9) without changing settings.
    /// </summary>
    public static IEndpointRouteBuilder MapForecastEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/clinics/{slug}/forecast", async (
            string slug,
            double? within,
            double? level,
            ClinicDb db,
            BoardService boards,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            var clinic = await db.Clinics.AsNoTracking().SingleOrDefaultAsync(c => c.Slug == slug, ct);
            if (clinic is null)
            {
                return VisitEndpoints.ClinicNotFound(slug);
            }

            var target = new ServiceTarget(within ?? clinic.TargetWaitMinutes, level ?? clinic.TargetServiceLevel);
            if (target.WithinMinutes is < 0 or > 480 || target.ServiceLevel is <= 0 or >= 1)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["target"] = ["within must be 0-480 minutes and level must be between 0 and 1 (exclusive)."],
                });
            }

            var desk = await boards.BuildAsync(slug, ct);
            var lambda = desk!.Model.ArrivalsPerHour;
            var visitMinutes = desk.Model.VisitMinutes;

            var recommended = StaffingPlanner.Recommend(lambda, visitMinutes, target)?.Providers;
            var top = Math.Max(Math.Max(recommended ?? 1, clinic.ProvidersOnShift) + 3, 6);
            var curve = StaffingPlanner.Curve(lambda, visitMinutes, target, 1, Math.Min(top, VisitEndpoints.MaxProviders))
                .Select(o => new CurvePointDto(
                    o.Providers,
                    o.Metrics.IsStable,
                    o.ServiceLevel,
                    o.Metrics.ProbabilityOfWaiting,
                    double.IsFinite(o.Metrics.MeanWaitMinutes) ? o.Metrics.MeanWaitMinutes : null,
                    o.MeetsTarget))
                .ToList();

            var profile = await boards.HourlyProfileAsync(clinic, clock.GetUtcNow().UtcDateTime, ct);
            var day = StaffingPlanner.PlanDay(profile, visitMinutes, target)
                .Select(h => new HourDto(h.Hour, Math.Round(h.ArrivalsPerHour, 2), h.RecommendedProviders))
                .ToList();

            return Results.Ok(new ForecastDto(
                slug,
                new TargetDto(target.WithinMinutes, target.ServiceLevel),
                lambda,
                visitMinutes,
                clinic.ProvidersOnShift,
                recommended,
                curve,
                day));
        }).RequireStaffKey();

        return app;
    }
}
