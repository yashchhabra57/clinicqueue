using ClinicQueue.Api.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Api.Board;

/// <summary>
/// Real-time channel for lobby screens and front-desk consoles. Each clinic is a SignalR group.
/// The hub only ever pushes the PUBLIC board (ticket codes, no initials), so it needs no auth;
/// front-desk screens treat each push as a cue to re-fetch the staff-only desk view.
/// </summary>
public sealed class BoardHub(ClinicDb db) : Hub
{
    public const string Path = "/hubs/board";
    public const string BoardMessage = "board";

    public static string Group(string slug) => $"clinic:{slug}";

    /// <summary>Subscribes the caller to a clinic and returns the current board so it can render immediately.</summary>
    public async Task<PublicBoard?> Join(string slug, BoardService boards)
    {
        ArgumentNullException.ThrowIfNull(boards);
        if (!await db.Clinics.AnyAsync(c => c.Slug == slug))
        {
            throw new HubException($"No clinic '{slug}'.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, Group(slug));
        var desk = await boards.BuildAsync(slug);
        return desk is null ? null : PublicBoard.From(desk);
    }

    public Task Leave(string slug) => Groups.RemoveFromGroupAsync(Context.ConnectionId, Group(slug));
}

/// <summary>Rebuilds the board after a change and pushes it to everyone watching that clinic.</summary>
public sealed class SignalRBoardNotifier(
    IHubContext<BoardHub> hub,
    IServiceScopeFactory scopes,
    ILogger<SignalRBoardNotifier> logger) : IBoardNotifier
{
    public async Task ClinicChangedAsync(string slug, CancellationToken ct = default)
    {
        try
        {
            // A fresh scope: the caller's DbContext may still be tracking the entities it just saved.
            await using var scope = scopes.CreateAsyncScope();
            var boards = scope.ServiceProvider.GetRequiredService<BoardService>();
            var desk = await boards.BuildAsync(slug, ct);
            if (desk is not null)
            {
                await hub.Clients.Group(BoardHub.Group(slug)).SendAsync(BoardHub.BoardMessage, PublicBoard.From(desk), ct);
            }
        }
#pragma warning disable CA1031 // A failed push must never fail the check-in that triggered it.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            logger.LogWarning(ex, "Could not push board update for {Slug}", slug);
        }
    }
}
