namespace ClinicQueue.Api.Board;

/// <summary>Tells connected screens that a clinic's queue changed.</summary>
public interface IBoardNotifier
{
    Task ClinicChangedAsync(string slug, CancellationToken ct = default);
}

public sealed class NullBoardNotifier : IBoardNotifier
{
    public Task ClinicChangedAsync(string slug, CancellationToken ct = default) => Task.CompletedTask;
}
