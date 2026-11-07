using JourneyPlanningApi.Models;
using Microsoft.EntityFrameworkCore;

namespace JourneyPlanningApi.Data;

/// <summary>EF Core equivalent of the SQLAlchemy models (users, journeys, api_usage_logs).</summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Journey> Journeys => Set<Journey>();
    public DbSet<ApiUsageLog> ApiUsageLogs => Set<ApiUsageLog>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        if (Database.IsNpgsql()) b.HasPostgresExtension("citext");

        b.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.FirstName).HasColumnName("first_name").HasMaxLength(100).IsRequired();
            e.Property(x => x.LastName).HasColumnName("last_name").HasMaxLength(100).IsRequired();
            var email = e.Property(x => x.Email).HasColumnName("email").HasMaxLength(255).IsRequired();
            // MySQL compares emails case-insensitively (default collation); citext gives PostgreSQL the same
            // behaviour for login lookups and the unique index.
            if (Database.IsNpgsql()) email.HasColumnType("citext");
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.PasswordHash).HasColumnName("password_hash").HasMaxLength(255).IsRequired();
            e.Property(x => x.Role).HasColumnName("role").HasMaxLength(20).IsRequired().HasDefaultValue("user");
            e.Property(x => x.CreatedAt).HasColumnName("created_at")
                .HasDefaultValueSql("CURRENT_TIMESTAMP").ValueGeneratedOnAdd();
        });

        b.Entity<Journey>(e =>
        {
            e.ToTable("journeys");
            e.HasKey(x => x.JourneyId);
            e.Property(x => x.JourneyId).HasColumnName("journey_id").ValueGeneratedOnAdd();
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.Destination).HasColumnName("destination").HasMaxLength(255).IsRequired();
            e.Property(x => x.StartDate).HasColumnName("start_date");
            e.Property(x => x.EndDate).HasColumnName("end_date");
            e.Property(x => x.Budget).HasColumnName("budget").HasPrecision(10, 2);
            e.Property(x => x.People).HasColumnName("people");
            e.Property(x => x.Notes).HasColumnName("notes");
            e.Property(x => x.CreatedAt).HasColumnName("created_at")
                .HasDefaultValueSql("CURRENT_TIMESTAMP").ValueGeneratedOnAdd();
            e.HasOne(x => x.User).WithMany(u => u.Journeys).HasForeignKey(x => x.UserId);
        });

        b.Entity<ApiUsageLog>(e =>
        {
            e.ToTable("api_usage_logs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.Endpoint).HasColumnName("endpoint").HasMaxLength(255).IsRequired();
            e.Property(x => x.Method).HasColumnName("method").HasMaxLength(10).IsRequired();
            e.Property(x => x.Parameters).HasColumnName("parameters");
            e.Property(x => x.StatusCode).HasColumnName("status_code");
            e.Property(x => x.CreatedAt).HasColumnName("created_at")
                .HasDefaultValueSql("CURRENT_TIMESTAMP").ValueGeneratedOnAdd();
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
        });
    }
}
