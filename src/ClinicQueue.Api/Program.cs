using ClinicQueue.Api.Auth;
using ClinicQueue.Api.Board;
using ClinicQueue.Api.Data;
using ClinicQueue.Api.Visits;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddClinicDatabase(builder.Configuration);
builder.Services.AddStaffAuth(builder.Configuration, builder.Environment);
builder.Services.AddSingleton<IBoardNotifier, NullBoardNotifier>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

await app.Services.InitializeDatabaseAsync();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
app.MapVisitEndpoints();

await app.RunAsync();

public partial class Program;
