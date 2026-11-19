using ClinicQueue.Api.Auth;
using ClinicQueue.Api.Data;
using ClinicQueue.Api.Visits;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Api.Board;

public static class BoardEndpoints
{
    public static IEndpointRouteBuilder MapBoardEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/clinics", async (ClinicDb db, CancellationToken ct) =>
            await db.Clinics.AsNoTracking()
                .OrderBy(c => c.Name)
                .Select(c => new { c.Slug, c.Name })
                .ToListAsync(ct));

        // Public: safe to show on a lobby TV or a phone in the parking lot.
        app.MapGet("/api/clinics/{slug}/board", async (string slug, BoardService boards, CancellationToken ct) =>
            await boards.BuildAsync(slug, ct) is { } desk
                ? Results.Ok(PublicBoard.From(desk))
                : VisitEndpoints.ClinicNotFound(slug));

        // Staff: includes initials, reasons and the model's inputs.
        app.MapGet("/api/clinics/{slug}/desk", async (string slug, BoardService boards, CancellationToken ct) =>
            await boards.BuildAsync(slug, ct) is { } desk
                ? Results.Ok(desk)
                : VisitEndpoints.ClinicNotFound(slug))
            .RequireStaffKey();

        return app;
    }
}
