using Microsoft.EntityFrameworkCore;

namespace AyazTekServis.Models;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<ServiceRecord> ServiceRecords => Set<ServiceRecord>();
    public DbSet<ServicePart> ServiceParts => Set<ServicePart>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<BackupHistory> BackupHistories => Set<BackupHistory>();
    public DbSet<ErrorLog> ErrorLogs => Set<ErrorLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>().Property(x => x.Username).UseCollation("NOCASE");
        modelBuilder.Entity<User>().HasIndex(x => x.Username).IsUnique();
        modelBuilder.Entity<User>().HasIndex(x => x.Email).IsUnique();
        modelBuilder.Entity<ServiceRecord>().HasIndex(x => x.TrackingNumber).IsUnique();
        modelBuilder.Entity<ServiceRecord>().HasIndex(x => x.Status);
        modelBuilder.Entity<ServiceRecord>().HasIndex(x => x.SerialNumber);
        modelBuilder.Entity<ServiceRecord>().HasIndex(x => x.CustomerEmail);
        modelBuilder.Entity<ServiceRecord>().HasIndex(x => x.CustomerPhone);
        modelBuilder.Entity<ServiceRecord>().HasIndex(x => new { x.IsDeleted, x.CreatedAt });
        modelBuilder.Entity<ServiceRecord>().HasIndex(x => new { x.IsDeleted, x.Status });
        modelBuilder.Entity<ServiceRecord>().HasIndex(x => new { x.IsDeleted, x.ServiceArrivalDate });
        modelBuilder.Entity<ServiceRecord>().HasIndex(x => new { x.IsDeleted, x.ServiceSentDate });
        modelBuilder.Entity<User>().HasIndex(x => x.CreatedAt);
        modelBuilder.Entity<AuditLog>().HasIndex(x => x.CreatedAt);
        modelBuilder.Entity<AuditLog>().HasIndex(x => new { x.UserId, x.CreatedAt });
        modelBuilder.Entity<AuditLog>().HasIndex(x => new { x.EntityType, x.EntityId });
        modelBuilder.Entity<PasswordResetToken>().HasIndex(x => x.TokenHash).IsUnique();
        modelBuilder.Entity<PasswordResetToken>().HasIndex(x => new { x.UserId, x.ExpiresAt });
        modelBuilder.Entity<UserSession>().HasIndex(x => x.SessionId).IsUnique();
        modelBuilder.Entity<UserSession>().HasIndex(x => new { x.UserId, x.RevokedAt, x.ExpiresAt });
        modelBuilder.Entity<BackupHistory>().HasIndex(x => x.CreatedAt);
        modelBuilder.Entity<ErrorLog>().HasIndex(x => x.CreatedAt);
        modelBuilder.Entity<ErrorLog>().HasIndex(x => new { x.UserId, x.CreatedAt });

        modelBuilder.Entity<ServiceRecord>()
            .HasMany(x => x.Parts)
            .WithOne(x => x.ServiceRecord)
            .HasForeignKey(x => x.ServiceRecordId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
