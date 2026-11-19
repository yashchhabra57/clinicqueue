using ClinicQueue.Api.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddClinicDatabase(builder.Configuration);

var app = builder.Build();

await app.Services.InitializeDatabaseAsync();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

await app.RunAsync();

public partial class Program;
