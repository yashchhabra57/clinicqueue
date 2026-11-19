using ClinicQueue.Api.Auth;
using ClinicQueue.Api.Board;
using ClinicQueue.Api.Data;
using ClinicQueue.Api.Visits;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddClinicDatabase(builder.Configuration);
builder.Services.AddStaffAuth(builder.Configuration, builder.Environment);
builder.Services.AddSignalR();
builder.Services.AddSingleton<IBoardNotifier, SignalRBoardNotifier>();
builder.Services.AddSingleton(builder.Configuration.GetSection("Board").Get<BoardOptions>() ?? new BoardOptions());
builder.Services.AddScoped<BoardService>();
builder.Services.AddHostedService<BoardTicker>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseDefaultFiles();
app.UseStaticFiles();

await app.Services.InitializeDatabaseAsync(seedDemo: app.Configuration.GetValue("Seed:Demo", true));

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
app.MapVisitEndpoints();
app.MapBoardEndpoints();
app.MapForecastEndpoints();
app.MapHub<BoardHub>(BoardHub.Path);

await app.RunAsync();

public partial class Program;
