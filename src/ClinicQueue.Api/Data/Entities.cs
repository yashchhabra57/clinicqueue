namespace ClinicQueue.Api.Data;

public sealed class Clinic
{
    public int Id { get; set; }

    /// <summary>URL-safe identifier, e.g. "northside".</summary>
    public required string Slug { get; set; }

    public required string Name { get; set; }

    /// <summary>IANA zone id such as "America/Chicago". Ticket numbers and hourly profiles use local time.</summary>
    public string TimeZoneId { get; set; } = "America/Chicago";

    public int ProvidersOnShift { get; set; } = 2;

    /// <summary>Visit length assumed before there is enough history.</summary>
    public double DefaultVisitMinutes { get; set; } = 15;

    /// <summary>Arrival rate assumed for hours with no history.</summary>
    public double FallbackArrivalsPerHour { get; set; } = 6;

    /// <summary>Service-level goal: <see cref="TargetServiceLevel"/> of patients seen within this many minutes.</summary>
    public double TargetWaitMinutes { get; set; } = 30;

    public double TargetServiceLevel { get; set; } = 0.8;

    /// <summary>Local date the ticket counter belongs to; the counter resets each morning.</summary>
    public DateOnly TicketDay { get; set; }

    public int LastTicketNumber { get; set; }

    /// <summary>
    /// Optimistic-concurrency token. Every operation that depends on room capacity or the ticket
    /// counter rewrites it, so two front-desk screens acting at the same moment can't both win.
    /// </summary>
    public Guid Version { get; set; } = Guid.NewGuid();

    public List<Visit> Visits { get; } = [];
}

public enum VisitStatus
{
    Waiting,
    InRoom,
    Completed,
    LeftWithoutBeingSeen,
}

public sealed class Visit
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public int ClinicId { get; set; }

    public Clinic? Clinic { get; set; }

    /// <summary>What the lobby screen shows instead of a name, e.g. "A-007".</summary>
    public required string TicketCode { get; set; }

    /// <summary>Front-desk only. Never sent to the public board.</summary>
    public required string Initials { get; set; }

    public string? Reason { get; set; }

    public VisitStatus Status { get; set; } = VisitStatus.Waiting;

    // All timestamps are UTC.
    public DateTime CheckedInAt { get; set; }

    public DateTime? CalledAt { get; set; }

    public DateTime? EndedAt { get; set; }

    public Guid Version { get; set; } = Guid.NewGuid();

    public double? VisitMinutes =>
        Status == VisitStatus.Completed && CalledAt is { } start && EndedAt is { } end
            ? (end - start).TotalMinutes
            : null;
}
