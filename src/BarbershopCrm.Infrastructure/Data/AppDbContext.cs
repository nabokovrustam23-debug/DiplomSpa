using BarbershopCrm.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BarbershopCrm.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Persona> Persona => Set<Persona>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<UserToken> UserTokens => Set<UserToken>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<Master> Masters => Set<Master>();
    public DbSet<MasterService> MasterServices => Set<MasterService>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<WorkSchedule> WorkSchedules => Set<WorkSchedule>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<Visit> Visits => Set<Visit>();
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<ConsentLogEntry> ConsentLog => Set<ConsentLogEntry>();
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        SeedData.Apply(modelBuilder);
    }
}
