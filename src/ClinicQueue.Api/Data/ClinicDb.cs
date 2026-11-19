using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Api.Data;

public sealed class ClinicDb(DbContextOptions<ClinicDb> options) : DbContext(options)
{
    public DbSet<Clinic> Clinics => Set<Clinic>();

    public DbSet<Visit> Visits => Set<Visit>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Clinic>(clinic =>
        {
            clinic.HasIndex(c => c.Slug).IsUnique();
            clinic.Property(c => c.Slug).HasMaxLength(40);
            clinic.Property(c => c.Name).HasMaxLength(120);
            clinic.Property(c => c.TimeZoneId).HasMaxLength(64);
            clinic.Property(c => c.Version).IsConcurrencyToken();
            clinic.HasMany(c => c.Visits).WithOne(v => v.Clinic).HasForeignKey(v => v.ClinicId);
        });

        modelBuilder.Entity<Visit>(visit =>
        {
            visit.Property(v => v.TicketCode).HasMaxLength(12);
            visit.Property(v => v.Initials).HasMaxLength(3);
            visit.Property(v => v.Reason).HasMaxLength(80);
            visit.Property(v => v.Status).HasConversion<string>().HasMaxLength(24);
            visit.Property(v => v.Version).IsConcurrencyToken();
            visit.Ignore(v => v.VisitMinutes);

            // The board query: "who is waiting / in a room at this clinic, oldest first".
            visit.HasIndex(v => new { v.ClinicId, v.Status, v.CheckedInAt });
            // Arrival history for the hour-of-day profile.
            visit.HasIndex(v => new { v.ClinicId, v.CheckedInAt });
        });
    }
}
