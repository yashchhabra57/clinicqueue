using System.Net.Http.Json;
using ClinicQueue.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace ClinicQueue.Api.Tests;

/// <summary>
/// Boots the real app in memory with a private SQLite database and a fake clock.
/// Set CLINICQUEUE_TEST_POSTGRES to a connection string to run the same tests on PostgreSQL
/// (CI does this against a service container).
/// </summary>
public sealed class ClinicApiFactory : WebApplicationFactory<Program>
{
    public const string StaffKey = "test-staff-key";
    public const string Slug = "northside";

    // 9:00 AM in Chicago (UTC-6 in November).
    public static readonly DateTimeOffset Start = new(2026, 11, 18, 15, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection? _keepAlive;
    private readonly string _connectionString;
    private readonly string _provider;

    public ClinicApiFactory()
    {
        var pg = Environment.GetEnvironmentVariable("CLINICQUEUE_TEST_POSTGRES");
        if (!string.IsNullOrWhiteSpace(pg))
        {
            _provider = "Postgres";
            // A fresh database per factory keeps test classes isolated.
            _connectionString = new Npgsql.NpgsqlConnectionStringBuilder(pg) { Database = $"cq_test_{Guid.NewGuid():N}" }.ConnectionString;
        }
        else
        {
            _provider = "Sqlite";
            _connectionString = $"Data Source=file:cq{Guid.NewGuid():N}?mode=memory&cache=shared";
            // An in-memory SQLite database lives only while a connection is open.
            _keepAlive = new SqliteConnection(_connectionString);
            _keepAlive.Open();
        }
    }

    public FakeTimeProvider Clock { get; } = new(Start);

    public bool IsPostgres => _provider == "Postgres";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:Provider", _provider);
        builder.UseSetting("ConnectionStrings:ClinicQueue", _connectionString);
        builder.UseSetting("Staff:ApiKey", StaffKey);
        builder.UseSetting("Seed:Demo", "false");
        builder.UseSetting("Board:TickSeconds", "0");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    public HttpClient StaffClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Staff-Key", StaffKey);
        return client;
    }

    public async Task SeedClinicAsync(int providers = 2, double visitMinutes = 15, double fallbackPerHour = 6)
    {
        _ = Server; // make sure the host (and schema) exist
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClinicDb>();
        db.Clinics.Add(new Clinic
        {
            Slug = Slug,
            Name = "Northside Walk-In",
            TimeZoneId = "America/Chicago",
            ProvidersOnShift = providers,
            DefaultVisitMinutes = visitMinutes,
            FallbackArrivalsPerHour = fallbackPerHour,
        });
        await db.SaveChangesAsync();
    }

    public async Task WithDbAsync(Func<ClinicDb, Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        await using var scope = Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<ClinicDb>());
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && IsPostgres && Server is not null)
        {
            using var scope = Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<ClinicDb>().Database.EnsureDeleted();
        }

        base.Dispose(disposing);
        if (disposing)
        {
            _keepAlive?.Dispose();
        }
    }
}

public static class HttpExtensions
{
    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        var value = await response.Content.ReadFromJsonAsync<T>();
        return value ?? throw new InvalidOperationException("Empty response body");
    }
}
