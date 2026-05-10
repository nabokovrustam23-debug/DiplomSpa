using BarbershopCrm.Domain.Entities;
using BarbershopCrm.Domain.Enums;
using BarbershopCrm.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BarbershopCrm.Tests.Notifications;

/// <summary>
/// Shared helper for notification tests: creates an in-memory SQLite DB and seeds
/// the minimal data needed (branch, service, master, client with email, admin, owner).
/// </summary>
public sealed class NotificationsTestFixture : IAsyncDisposable
{
    public SqliteConnection Connection { get; }
    public DbContextOptions<AppDbContext> Options { get; }

    public int BranchId { get; private set; }
    public int ServiceId { get; private set; }
    public int MasterId { get; private set; }
    public int MasterPersonaId { get; private set; }
    public int ClientId { get; private set; }
    public int ClientPersonaId { get; private set; }
    public int ClientUserId { get; private set; }
    public int ClientNoEmailPersonaId { get; private set; }
    public int AdminPersonaId { get; private set; }
    public int OwnerPersonaId { get; private set; }

    private NotificationsTestFixture()
    {
        Connection = new SqliteConnection("Data Source=:memory:;Cache=Shared");
        Connection.Open();
        Options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(Connection).Options;
    }

    public static async Task<NotificationsTestFixture> CreateAsync()
    {
        var fx = new NotificationsTestFixture();
        await using var db = new AppDbContext(fx.Options);
        await db.Database.EnsureCreatedAsync();
        await fx.SeedAsync(db);
        return fx;
    }

    public AppDbContext NewContext() => new(Options);

    private async Task SeedAsync(AppDbContext db)
    {
        var branch = await db.Branches.OrderBy(b => b.BranchId).FirstAsync();
        BranchId = branch.BranchId;

        var svc = new Service { Name = "Стрижка", DurationMinutes = 60, Price = 1500m, IsActive = true };
        db.Services.Add(svc);
        await db.SaveChangesAsync();
        ServiceId = svc.ServiceId;

        var clientRole = (await db.Roles.FirstAsync(r => r.Code == RoleCode.Client)).RoleId;
        var masterRole = (await db.Roles.FirstAsync(r => r.Code == RoleCode.Master)).RoleId;
        var adminRole = (await db.Roles.FirstAsync(r => r.Code == RoleCode.Admin)).RoleId;
        var ownerRole = (await db.Roles.FirstAsync(r => r.Code == RoleCode.Owner)).RoleId;

        // Master with email
        var mp = new Persona { LastName = "Иванов", FirstName = "Алексей", Phone = "+79180000001", Email = "master@thq.test" };
        db.Persona.Add(mp);
        await db.SaveChangesAsync();
        MasterPersonaId = mp.PersonaId;

        var master = new Master { PersonaId = mp.PersonaId, BranchId = BranchId, Position = "Барбер", HireDate = new DateOnly(2024, 1, 1), IsActive = true };
        db.Masters.Add(master);
        await db.SaveChangesAsync();
        MasterId = master.MasterId;
        db.MasterServices.Add(new MasterService { MasterId = MasterId, ServiceId = ServiceId });

        // Client with email
        var cp = new Persona { LastName = "Петров", FirstName = "Михаил", Phone = "+79180000002", Email = "client@thq.test" };
        db.Persona.Add(cp);
        await db.SaveChangesAsync();
        ClientPersonaId = cp.PersonaId;
        var client = new Client { PersonaId = cp.PersonaId };
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        ClientId = client.ClientId;
        var cu = new User { PersonaId = cp.PersonaId, RoleId = clientRole, Login = "client@thq.test", PasswordHash = "x", PasswordSalt = "x" };
        db.Users.Add(cu);

        // Client without email
        var cpn = new Persona { LastName = "Без", FirstName = "Почты", Phone = "+79180000003" };
        db.Persona.Add(cpn);
        await db.SaveChangesAsync();
        ClientNoEmailPersonaId = cpn.PersonaId;
        db.Clients.Add(new Client { PersonaId = cpn.PersonaId });

        // Admin (in same branch)
        var ap = new Persona { LastName = "Адмов", FirstName = "Игорь", Phone = "+79180000010", Email = "admin@thq.test" };
        db.Persona.Add(ap);
        await db.SaveChangesAsync();
        AdminPersonaId = ap.PersonaId;
        db.Users.Add(new User { PersonaId = ap.PersonaId, RoleId = adminRole, BranchId = BranchId, Login = "admin@thq.test", PasswordHash = "x", PasswordSalt = "x" });

        // Owner
        var op = new Persona { LastName = "Влад", FirstName = "Имир", Phone = "+79180000020", Email = "owner@thq.test" };
        db.Persona.Add(op);
        await db.SaveChangesAsync();
        OwnerPersonaId = op.PersonaId;
        db.Users.Add(new User { PersonaId = op.PersonaId, RoleId = ownerRole, Login = "owner@thq.test", PasswordHash = "x", PasswordSalt = "x" });

        await db.SaveChangesAsync();

        ClientUserId = cu.UserId;
    }

    public async Task<int> CreateBookingAsync(DateTime startUtc, string status = BookingStatus.Created, int? clientId = null)
    {
        await using var db = NewContext();
        var booking = new Booking
        {
            ClientId = clientId ?? ClientId,
            BranchId = BranchId,
            ServiceId = ServiceId,
            MasterId = MasterId,
            StartDateTime = startUtc,
            DurationMinutes = 60,
            Status = status,
            Source = BookingSource.Online,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        return booking.BookingId;
    }

    public async ValueTask DisposeAsync()
    {
        await Connection.DisposeAsync();
    }
}
