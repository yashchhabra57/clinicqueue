using ClinicQueue.Api.Data;

namespace ClinicQueue.Api.Visits;

public enum VisitAction
{
    Call,
    Complete,
    Leave,
    ReturnToQueue,
}

/// <summary>
/// The allowed status changes for a visit. Keeping the table in one place means the API,
/// the tests and the README all describe the same state machine:
///
///   Waiting --Call--> InRoom --Complete--> Completed
///   Waiting --Leave--> LeftWithoutBeingSeen
///   InRoom  --ReturnToQueue--> Waiting   (called by mistake; keeps their place in line)
/// </summary>
public static class VisitWorkflow
{
    private static readonly Dictionary<(VisitStatus, VisitAction), VisitStatus> Transitions = new()
    {
        [(VisitStatus.Waiting, VisitAction.Call)] = VisitStatus.InRoom,
        [(VisitStatus.Waiting, VisitAction.Leave)] = VisitStatus.LeftWithoutBeingSeen,
        [(VisitStatus.InRoom, VisitAction.Complete)] = VisitStatus.Completed,
        [(VisitStatus.InRoom, VisitAction.ReturnToQueue)] = VisitStatus.Waiting,
    };

    public static bool CanApply(VisitStatus from, VisitAction action) => Transitions.ContainsKey((from, action));

    /// <summary>Applies the action and stamps timestamps. Returns false (and changes nothing) if not allowed.</summary>
    public static bool TryApply(Visit visit, VisitAction action, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(visit);
        if (!Transitions.TryGetValue((visit.Status, action), out var next))
        {
            return false;
        }

        switch (action)
        {
            case VisitAction.Call:
                visit.CalledAt = utcNow;
                break;
            case VisitAction.Complete:
            case VisitAction.Leave:
                visit.EndedAt = utcNow;
                break;
            case VisitAction.ReturnToQueue:
                // CheckedInAt is untouched, so they go back to their original spot in line.
                visit.CalledAt = null;
                break;
        }

        visit.Status = next;
        visit.Version = Guid.NewGuid();
        return true;
    }
}
