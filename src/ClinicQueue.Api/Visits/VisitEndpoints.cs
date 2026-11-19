using System.Text.RegularExpressions;
using ClinicQueue.Api.Auth;
using ClinicQueue.Api.Board;
using ClinicQueue.Api.Clinics;
using ClinicQueue.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Api.Visits;

public sealed record CheckInRequest(string? Initials, string? Reason);

public sealed record StaffingRequest(int ProvidersOnShift);

public sealed record VisitDto(
    Guid Id,
    string TicketCode,
    string Initials,
    string? Reason,
    string Status,
    DateTime CheckedInAt,
    DateTime? CalledAt,
    DateTime? EndedAt)
{
    public static VisitDto From(Visit v) =>
        new(v.Id, v.TicketCode, v.Initials, v.Reason, v.Status.ToString(), v.CheckedInAt, v.CalledAt, v.EndedAt);
}

public static partial class VisitEndpoints
{
    public const int MaxProviders = 50;

    [GeneratedRegex("^[A-Za-z]{1,3}$")]
    private static partial Regex InitialsPattern();

    public static RouteGroupBuilder MapVisitEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/clinics/{slug}").RequireStaffKey();

        group.MapPost("/visits", CheckIn);
        group.MapPost("/visits/call-next", CallNext);
        group.MapPost("/visits/{id:guid}/call", (string slug, Guid id, ClinicDb db, TimeProvider clock, IBoardNotifier n, CancellationToken ct) =>
            Call(slug, id, db, clock, n, ct));
        group.MapPost("/visits/{id:guid}/complete", (string slug, Guid id, ClinicDb db, TimeProvider clock, IBoardNotifier n, CancellationToken ct) =>
            Transition(slug, id, VisitAction.Complete, db, clock, n, ct));
        group.MapPost("/visits/{id:guid}/leave", (string slug, Guid id, ClinicDb db, TimeProvider clock, IBoardNotifier n, CancellationToken ct) =>
            Transition(slug, id, VisitAction.Leave, db, clock, n, ct));
        group.MapPost("/visits/{id:guid}/return", (string slug, Guid id, ClinicDb db, TimeProvider clock, IBoardNotifier n, CancellationToken ct) =>
            Transition(slug, id, VisitAction.ReturnToQueue, db, clock, n, ct));
        group.MapPut("/staffing", SetStaffing);

        return group;
    }

    private static async Task<IResult> CheckIn(
        string slug, CheckInRequest body, ClinicDb db, TimeProvider clock, IBoardNotifier notifier, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var initials = body.Initials?.Trim() ?? string.Empty;
        if (!InitialsPattern().IsMatch(initials))
        {
            errors["initials"] = ["Use 1 to 3 letters (initials only, never a full name)."];
        }

        var reason = string.IsNullOrWhiteSpace(body.Reason) ? null : body.Reason.Trim();
        if (reason is { Length: > 80 })
        {
            errors["reason"] = ["Keep the reason under 80 characters."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var clinic = await db.Clinics.AsNoTracking().SingleOrDefaultAsync(c => c.Slug == slug, ct);
        if (clinic is null)
        {
            return ClinicNotFound(slug);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(ClinicClock.ToLocal(clinic, now));

        // Ticket numbers come from a counter on the clinic row. A read-modify-write in C# would let
        // two desks hand out the same number (or, with a concurrency token, make one of them fail
        // and retry, which falls apart under a burst). Instead the database does the increment in a
        // single UPDATE, which row-locks on PostgreSQL and serializes on SQLite, so simultaneous
        // check-ins simply queue up for a few milliseconds. In SQL, every SET expression sees the
        // old row, so the CASE resets the counter on the first check-in of a new local day.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Clinics
            .Where(c => c.Id == clinic.Id)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(c => c.LastTicketNumber, c => c.TicketDay == today ? c.LastTicketNumber + 1 : 1)
                    .SetProperty(c => c.TicketDay, today),
                ct);
        var number = await db.Clinics.Where(c => c.Id == clinic.Id).Select(c => c.LastTicketNumber).SingleAsync(ct);

        var visit = new Visit
        {
            ClinicId = clinic.Id,
            TicketCode = FormatTicket(number),
            Initials = initials.ToUpperInvariant(),
            Reason = reason,
            CheckedInAt = now,
        };
        db.Visits.Add(visit);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        await notifier.ClinicChangedAsync(slug, ct);
        return Results.Created($"/api/clinics/{slug}/visits/{visit.Id}", VisitDto.From(visit));
    }

    /// <summary>Calls the patient who has waited longest. FIFO keeps the posted estimates honest.</summary>
    private static async Task<IResult> CallNext(
        string slug, ClinicDb db, TimeProvider clock, IBoardNotifier notifier, CancellationToken ct)
    {
        var clinicId = await db.Clinics.Where(c => c.Slug == slug).Select(c => (int?)c.Id).SingleOrDefaultAsync(ct);
        if (clinicId is null)
        {
            return ClinicNotFound(slug);
        }

        var nextId = await db.Visits
            .Where(v => v.ClinicId == clinicId && v.Status == VisitStatus.Waiting)
            .OrderBy(v => v.CheckedInAt)
            .Select(v => (Guid?)v.Id)
            .FirstOrDefaultAsync(ct);

        return nextId is null
            ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Nobody is waiting")
            : await Call(slug, nextId.Value, db, clock, notifier, ct);
    }

    private static async Task<IResult> Call(
        string slug, Guid id, ClinicDb db, TimeProvider clock, IBoardNotifier notifier, CancellationToken ct)
    {
        var clinic = await db.Clinics.SingleOrDefaultAsync(c => c.Slug == slug, ct);
        if (clinic is null)
        {
            return ClinicNotFound(slug);
        }

        var visit = await db.Visits.SingleOrDefaultAsync(v => v.Id == id && v.ClinicId == clinic.Id, ct);
        if (visit is null)
        {
            return VisitNotFound(id);
        }

        var inRoom = await db.Visits.CountAsync(v => v.ClinicId == clinic.Id && v.Status == VisitStatus.InRoom, ct);
        if (inRoom >= clinic.ProvidersOnShift)
        {
            return Conflict($"All {clinic.ProvidersOnShift} providers are with patients. Complete a visit first.");
        }

        if (!VisitWorkflow.TryApply(visit, VisitAction.Call, clock.GetUtcNow().UtcDateTime))
        {
            return Conflict($"Visit {visit.TicketCode} is {visit.Status} and cannot be called.");
        }

        // Rewriting the clinic's version makes two simultaneous calls for the last free room collide:
        // the second SaveChanges sees a stale version and is rejected instead of over-filling rooms.
        clinic.Version = Guid.NewGuid();
        if (!await TrySaveAsync(db, ct))
        {
            return Conflict("Someone else updated the queue at the same moment. Refresh and try again.");
        }

        await notifier.ClinicChangedAsync(slug, ct);
        return Results.Ok(VisitDto.From(visit));
    }

    private static async Task<IResult> Transition(
        string slug, Guid id, VisitAction action, ClinicDb db, TimeProvider clock, IBoardNotifier notifier, CancellationToken ct)
    {
        var visit = await db.Visits.SingleOrDefaultAsync(v => v.Id == id && v.Clinic!.Slug == slug, ct);
        if (visit is null)
        {
            return VisitNotFound(id);
        }

        if (!VisitWorkflow.TryApply(visit, action, clock.GetUtcNow().UtcDateTime))
        {
            return Conflict($"Visit {visit.TicketCode} is {visit.Status}; '{action}' is not allowed.");
        }

        if (!await TrySaveAsync(db, ct))
        {
            return Conflict("This visit was changed by someone else. Refresh and try again.");
        }

        await notifier.ClinicChangedAsync(slug, ct);
        return Results.Ok(VisitDto.From(visit));
    }

    private static async Task<IResult> SetStaffing(
        string slug, StaffingRequest body, ClinicDb db, IBoardNotifier notifier, CancellationToken ct)
    {
        if (body.ProvidersOnShift is < 0 or > MaxProviders)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["providersOnShift"] = [$"Must be between 0 and {MaxProviders}."],
            });
        }

        var clinic = await db.Clinics.SingleOrDefaultAsync(c => c.Slug == slug, ct);
        if (clinic is null)
        {
            return ClinicNotFound(slug);
        }

        clinic.ProvidersOnShift = body.ProvidersOnShift;
        clinic.Version = Guid.NewGuid();
        if (!await TrySaveAsync(db, ct))
        {
            return Conflict("The clinic was updated at the same moment. Please try again.");
        }

        await notifier.ClinicChangedAsync(slug, ct);
        return Results.Ok(new { clinic.Slug, clinic.ProvidersOnShift });
    }

    public static string FormatTicket(int number) => $"A-{number:000}";

    private static async Task<bool> TrySaveAsync(ClinicDb db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }

    internal static IResult ClinicNotFound(string slug) =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Clinic not found", detail: $"No clinic '{slug}'.");

    private static IResult VisitNotFound(Guid id) =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Visit not found", detail: $"No visit {id} at this clinic.");

    private static IResult Conflict(string detail) =>
        Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Conflict", detail: detail);
}
