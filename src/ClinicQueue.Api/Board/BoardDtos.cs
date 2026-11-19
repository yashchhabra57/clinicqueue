namespace ClinicQueue.Api.Board;

public sealed record RangeDto(double Mean, double Low, double High);

public sealed record TargetDto(double WithinMinutes, double ServiceLevel);

/// <summary>Why the board believes what it believes. Shown on the front-desk screen.</summary>
public sealed record ModelDto(
    double ArrivalsPerHour,
    double RecentArrivalsPerHour,
    double HistoricalArrivalsPerHour,
    int RecentArrivals,
    double WindowMinutes,
    double VisitMinutes,
    int VisitSamples,
    double? Utilization,
    double ProbabilityOfWaiting,
    double? MeanWaitMinutes,
    double ServiceLevel,
    bool IsStable,
    int? RecommendedProviders);

public sealed record QueueEntryDto(
    Guid Id,
    string TicketCode,
    string Initials,
    string? Reason,
    int Position,
    DateTime CheckedInAt,
    double WaitedMinutes,
    RangeDto? EstimatedWait);

public sealed record RoomDto(Guid Id, string TicketCode, string Initials, string? Reason, DateTime CalledAt, double MinutesInRoom);

/// <summary>Everything the front desk sees. Contains initials, so staff-only.</summary>
public sealed record DeskBoard(
    string Slug,
    string Name,
    DateTime GeneratedAt,
    int ProvidersOnShift,
    TargetDto Target,
    ModelDto Model,
    RangeDto? WalkInEstimate,
    IReadOnlyList<QueueEntryDto> Waiting,
    IReadOnlyList<RoomDto> InRoom);

public sealed record PublicTicketDto(string TicketCode, int Position, RangeDto? EstimatedWait);

/// <summary>What the lobby TV shows: ticket codes and ranges only, no initials or reasons.</summary>
public sealed record PublicBoard(
    string Slug,
    string Name,
    DateTime GeneratedAt,
    int ProvidersOnShift,
    int Waiting,
    int InRoom,
    RangeDto? WalkInEstimate,
    IReadOnlyList<PublicTicketDto> Queue,
    IReadOnlyList<string> NowServing)
{
    public static PublicBoard From(DeskBoard desk)
    {
        ArgumentNullException.ThrowIfNull(desk);
        return new PublicBoard(
            desk.Slug,
            desk.Name,
            desk.GeneratedAt,
            desk.ProvidersOnShift,
            desk.Waiting.Count,
            desk.InRoom.Count,
            desk.WalkInEstimate,
            [.. desk.Waiting.Select(w => new PublicTicketDto(w.TicketCode, w.Position, w.EstimatedWait))],
            [.. desk.InRoom.OrderByDescending(r => r.CalledAt).Select(r => r.TicketCode)]);
    }
}
