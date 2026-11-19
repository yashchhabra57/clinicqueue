using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ClinicQueue.Api.Data;

/// <summary>
/// Used only by `dotnet ef migrations add`. Migrations target PostgreSQL, the production database;
/// no connection is opened while generating them.
/// </summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<ClinicDb>
{
    public ClinicDb CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ClinicDb>()
            .UseNpgsql("Host=localhost;Database=clinicqueue;Username=postgres")
            .Options;
        return new ClinicDb(options);
    }
}
