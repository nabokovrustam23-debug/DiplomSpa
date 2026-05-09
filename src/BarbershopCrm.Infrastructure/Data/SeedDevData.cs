using BarbershopCrm.Domain.Entities;
using BarbershopCrm.Domain.Enums;
using BarbershopCrm.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BarbershopCrm.Infrastructure.Data;

/// <summary>
/// Idempotent runtime seeder that inserts a fixed set of test users on first launch.
/// Should ONLY be run in Development. Test password is the same for every account
/// — see <see cref="TestPassword"/>.
/// </summary>
public static class SeedDevData
{
    /// <summary>Single, well-known dev password. NOT for production use.</summary>
    public const string TestPassword = "Test12345!";

    public static async Task ApplyAsync(
        AppDbContext db,
        IPasswordHasher hasher,
        ILogger logger,
        CancellationToken ct = default)
    {
        if (await db.Users.AnyAsync(ct))
        {
            logger.LogInformation("SeedDevData: users already present, skipping");
            return;
        }

        logger.LogInformation("SeedDevData: seeding test users with shared password '{Password}'", TestPassword);

        var ownerRoleId  = await db.Roles.Where(r => r.Code == RoleCode.Owner ).Select(r => r.RoleId).SingleAsync(ct);
        var adminRoleId  = await db.Roles.Where(r => r.Code == RoleCode.Admin ).Select(r => r.RoleId).SingleAsync(ct);
        var masterRoleId = await db.Roles.Where(r => r.Code == RoleCode.Master).Select(r => r.RoleId).SingleAsync(ct);
        var clientRoleId = await db.Roles.Where(r => r.Code == RoleCode.Client).Select(r => r.RoleId).SingleAsync(ct);

        var branchIds = await db.Branches.OrderBy(b => b.BranchId).Select(b => b.BranchId).ToListAsync(ct);
        if (branchIds.Count < 2)
            throw new InvalidOperationException("SeedDevData requires at least 2 branches.");
        var branch1 = branchIds[0];
        var branch2 = branchIds[1];

        var hash = hasher.Hash(TestPassword);
        var now = DateTime.UtcNow;

        // ---- Owner ---------------------------------------------------------
        Add(db, hash, now, ownerRoleId, branchId: null,
            "owner@thq.ru", "Тихий", "Михаил", "Сергеевич", "+79180000001", emailConfirmed: true);

        // ---- Admins (1 per branch) ----------------------------------------
        Add(db, hash, now, adminRoleId, branchId: branch1,
            "admin1@thq.ru", "Сергеев", "Иван", "Петрович", "+79180000010", emailConfirmed: true);
        Add(db, hash, now, adminRoleId, branchId: branch2,
            "admin2@thq.ru", "Петров", "Алексей", "Иванович", "+79180000011", emailConfirmed: true);

        // ---- Masters (2 per branch) — also create Master entities ----------
        AddMaster(db, hash, now, masterRoleId, branch1,
            "master1@thq.ru", "Кузнецов", "Артём", "Олегович", "+79180000020",
            position: "Старший барбер", hireDate: new DateOnly(2023, 5, 1));
        AddMaster(db, hash, now, masterRoleId, branch1,
            "master2@thq.ru", "Морозов", "Денис", "Викторович", "+79180000021",
            position: "Барбер", hireDate: new DateOnly(2024, 2, 15));
        AddMaster(db, hash, now, masterRoleId, branch2,
            "master3@thq.ru", "Волков", "Илья", "Андреевич", "+79180000022",
            position: "Барбер", hireDate: new DateOnly(2024, 7, 1));
        AddMaster(db, hash, now, masterRoleId, branch2,
            "master4@thq.ru", "Соколов", "Никита", "Сергеевич", "+79180000023",
            position: "Барбер", hireDate: new DateOnly(2025, 1, 10));

        // ---- Clients (3 demo) — also create Client entities ----------------
        AddClient(db, hash, now, clientRoleId,
            "client1@thq.ru", "Иванов", "Михаил", "Викторович", "+79180000030", emailConfirmed: true);
        AddClient(db, hash, now, clientRoleId,
            "client2@thq.ru", "Смирнов", "Олег", null, "+79180000031", emailConfirmed: true);
        AddClient(db, hash, now, clientRoleId,
            "client3@thq.ru", "Кравцов", "Игнат", "Петрович", "+79180000032",
            emailConfirmed: false /* демо для проверки сценария «не подтверждённый email» */);

        await db.SaveChangesAsync(ct);
        logger.LogInformation("SeedDevData: seeded {Count} test users", await db.Users.CountAsync(ct));
    }

    private static User Add(
        AppDbContext db,
        PasswordHash hash,
        DateTime now,
        int roleId,
        int? branchId,
        string login, string lastName, string firstName, string? middleName,
        string phone, bool emailConfirmed)
    {
        var persona = new Persona
        {
            LastName = lastName,
            FirstName = firstName,
            MiddleName = middleName,
            Phone = phone,
            Email = login,
        };
        var user = new User
        {
            Persona = persona,
            RoleId = roleId,
            BranchId = branchId,
            Login = login,
            PasswordHash = hash.HashBase64,
            PasswordSalt = hash.SaltBase64,
            PasswordIterations = hash.Iterations,
            IsEmailConfirmed = emailConfirmed,
            IsActive = true,
            CreatedAt = now,
        };
        db.Persona.Add(persona);
        db.Users.Add(user);
        return user;
    }

    private static void AddMaster(
        AppDbContext db,
        PasswordHash hash,
        DateTime now,
        int masterRoleId,
        int branchId,
        string login, string lastName, string firstName, string? middleName, string phone,
        string position, DateOnly hireDate)
    {
        var user = Add(db, hash, now, masterRoleId, branchId, login, lastName, firstName, middleName, phone,
            emailConfirmed: true);
        db.Masters.Add(new Master
        {
            Persona = user.Persona,
            BranchId = branchId,
            Position = position,
            HireDate = hireDate,
            IsActive = true,
        });
    }

    private static void AddClient(
        AppDbContext db,
        PasswordHash hash,
        DateTime now,
        int clientRoleId,
        string login, string lastName, string firstName, string? middleName, string phone,
        bool emailConfirmed)
    {
        var user = Add(db, hash, now, clientRoleId, branchId: null,
            login, lastName, firstName, middleName, phone, emailConfirmed);
        db.Clients.Add(new Client
        {
            Persona = user.Persona,
            Source = "seed",
            CreatedAt = now,
        });
    }
}
