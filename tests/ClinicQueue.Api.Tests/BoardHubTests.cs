using System.Net.Http.Json;
using System.Threading.Channels;
using ClinicQueue.Api.Board;
using ClinicQueue.Api.Visits;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace ClinicQueue.Api.Tests;

public sealed class BoardHubTests : IAsyncLifetime, IDisposable
{
    private readonly ClinicApiFactory _factory = new();
    private HubConnection _hub = null!;
    private readonly Channel<PublicBoard> _pushes = Channel.CreateUnbounded<PublicBoard>();

    public async Task InitializeAsync()
    {
        await _factory.SeedClinicAsync(providers: 1);
        _hub = new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, BoardHub.Path), o =>
            {
                // The in-memory test server has no real sockets, so use long polling through its handler.
                o.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
            })
            .Build();
        _hub.On<PublicBoard>(BoardHub.BoardMessage, board => _pushes.Writer.TryWrite(board));
        await _hub.StartAsync();
    }

    public async Task DisposeAsync() => await _hub.DisposeAsync();

    public void Dispose() => _factory.Dispose();

    private async Task<PublicBoard> NextPush()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        return await _pushes.Reader.ReadAsync(timeout.Token);
    }

    [Fact]
    public async Task Join_returns_the_current_board()
    {
        var board = await _hub.InvokeAsync<PublicBoard>("Join", ClinicApiFactory.Slug);
        Assert.Equal("Northside Walk-In", board.Name);
        Assert.Equal(0, board.Waiting);
        Assert.NotNull(board.WalkInEstimate);
    }

    [Fact]
    public async Task Joining_an_unknown_clinic_fails()
    {
        await Assert.ThrowsAsync<HubException>(() => _hub.InvokeAsync<PublicBoard>("Join", "nowhere"));
    }

    [Fact]
    public async Task Check_in_and_call_are_pushed_to_watchers()
    {
        await _hub.InvokeAsync<PublicBoard>("Join", ClinicApiFactory.Slug);
        using var staff = _factory.StaffClient();

        await staff.PostAsJsonAsync($"/api/clinics/{ClinicApiFactory.Slug}/visits", new CheckInRequest("AB", "cough"));
        var afterCheckIn = await NextPush();
        Assert.Equal(1, afterCheckIn.Waiting);
        Assert.Equal("A-001", afterCheckIn.Queue[0].TicketCode);

        await staff.PostAsync($"/api/clinics/{ClinicApiFactory.Slug}/visits/call-next", null);
        var afterCall = await NextPush();
        Assert.Equal(0, afterCall.Waiting);
        Assert.Equal(["A-001"], afterCall.NowServing);
    }
}
