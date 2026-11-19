using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Api.Data;

public static class DatabaseSetup
{
    /// <summary>
    /// "Sqlite" (default, zero setup) or "Postgres" (production). Postgres schema changes go
    /// through EF Core migrations; SQLite is created from the model for local dev and tests.
    /// </summary>
    public static IServiceCollection AddClinicDatabase(this IServiceCollection services, IConfiguration config)
    {
        var provider = config["Database:Provider"] ?? "Sqlite";
        var connection = config.GetConnectionString("ClinicQueue") ?? "Data Source=clinicqueue.db";

        services.AddDbContext<ClinicDb>(options =>
        {
            if (provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
            {
                options.UseNpgsql(connection);
            }
            else if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                options.UseSqlite(connection);
            }
            else
            {
                throw new InvalidOperationException($"Unknown Database:Provider '{provider}'. Use Sqlite or Postgres.");
            }
        });

        return services;
    }

    public static async Task InitializeDatabaseAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClinicDb>();
        if (db.Database.IsNpgsql())
        {
            await db.Database.MigrateAsync(ct);
        }
        else
        {
            await db.Database.EnsureCreatedAsync(ct);
        }
    }
}
