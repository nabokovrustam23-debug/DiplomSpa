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
            logger.LogInformation("SeedDevData: users already present, skipping user seed");
            await SeedSchedulesAsync(db, logger, ct);
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
            position: "Старший барбер", hireDate: new DateOnly(2023, 5, 1),
            serviceIds: new[] { 1, 2, 3, 4, 5 });
        AddMaster(db, hash, now, masterRoleId, branch1,
            "master2@thq.ru", "Морозов", "Денис", "Викторович", "+79180000021",
            position: "Барбер", hireDate: new DateOnly(2024, 2, 15),
            serviceIds: new[] { 1, 2, 4 });
        AddMaster(db, hash, now, masterRoleId, branch2,
            "master3@thq.ru", "Волков", "Илья", "Андреевич", "+79180000022",
            position: "Барбер", hireDate: new DateOnly(2024, 7, 1),
            serviceIds: new[] { 1, 3, 4, 5 });
        AddMaster(db, hash, now, masterRoleId, branch2,
            "master4@thq.ru", "Соколов", "Никита", "Сергеевич", "+79180000023",
            position: "Барбер", hireDate: new DateOnly(2025, 1, 10),
            serviceIds: new[] { 1, 2 });

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

        await SeedSchedulesAsync(db, logger, ct);
        await SeedBookingsAsync(db, logger, ct);
        await SeedLeadsAsync(db, logger, ct);
    }

    private static async Task SeedBookingsAsync(AppDbContext db, ILogger logger, CancellationToken ct)
    {
        if (await db.Bookings.AnyAsync(ct))
        {
            logger.LogInformation("SeedDevData: bookings already present, skipping");
            return;
        }

        var clients = await db.Clients
            .Include(c => c.Persona)
            .Where(c => c.Persona.User != null && c.Persona.User.RoleId != 0)
            .ToListAsync(ct);
        if (clients.Count == 0) return;
        var firstClient = clients[0];

        var masters = await db.Masters.Include(m => m.Persona).Where(m => m.IsActive)
            .ToListAsync(ct);
        if (masters.Count == 0) return;

        var services = await db.Services.Where(s => s.IsActive).ToListAsync(ct);
        if (services.Count == 0) return;

        var today = DateTime.Today;
        var futureDay = today.AddDays(1);
        // Skip Sunday (master is off).
        while (futureDay.DayOfWeek == DayOfWeek.Sunday) futureDay = futureDay.AddDays(1);
        var pastDay = today.AddDays(-3);
        while (pastDay.DayOfWeek == DayOfWeek.Sunday) pastDay = pastDay.AddDays(-1);

        var pickService = services.First();
        var pickMaster = masters.FirstOrDefault(m => db.MasterServices.Any(ms => ms.MasterId == m.MasterId && ms.ServiceId == pickService.ServiceId)) ?? masters[0];

        // Future booking — Created
        db.Bookings.Add(new Booking
        {
            ClientId = firstClient.ClientId,
            MasterId = pickMaster.MasterId,
            ServiceId = pickService.ServiceId,
            BranchId = pickMaster.BranchId,
            StartDateTime = futureDay.AddHours(11),
            DurationMinutes = pickService.DurationMinutes,
            PriceSnapshot = pickService.Price,
            Status = BookingStatus.Created,
            Source = BookingSource.Online,
        });

        // Past completed booking + Visit
        var completed = new Booking
        {
            ClientId = firstClient.ClientId,
            MasterId = pickMaster.MasterId,
            ServiceId = pickService.ServiceId,
            BranchId = pickMaster.BranchId,
            StartDateTime = pastDay.AddHours(12),
            DurationMinutes = pickService.DurationMinutes,
            PriceSnapshot = pickService.Price,
            Status = BookingStatus.Completed,
            Source = BookingSource.Online,
        };
        db.Bookings.Add(completed);
        await db.SaveChangesAsync(ct);
        db.Visits.Add(new Visit
        {
            BookingId = completed.BookingId,
            TotalAmount = pickService.Price,
            MasterNotes = "Тестовый визит из сидинга.",
            CompletedAt = pastDay.AddHours(12).AddMinutes(pickService.DurationMinutes),
        });
        await db.SaveChangesAsync(ct);
        logger.LogInformation("SeedDevData: seeded sample bookings");
    }

    private static async Task SeedLeadsAsync(AppDbContext db, ILogger logger, CancellationToken ct)
    {
        if (await db.Leads.AnyAsync(ct))
        {
            logger.LogInformation("SeedDevData: leads already present, skipping");
            return;
        }

        var firstBranch = await db.Branches.OrderBy(b => b.BranchId).Select(b => (int?)b.BranchId).FirstOrDefaultAsync(ct);
        db.Leads.Add(new Domain.Entities.Lead
        {
            RawName = "Гость Тест",
            RawPhone = "+79180000099",
            PreferredBranchId = firstBranch,
            Comment = "Хочу записаться, перезвоните пожалуйста.",
            Status = LeadStatus.New,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        logger.LogInformation("SeedDevData: seeded sample leads");
    }

    private static async Task SeedSchedulesAsync(AppDbContext db, ILogger logger, CancellationToken ct)
    {
        if (await db.WorkSchedules.AnyAsync(ct))
        {
            // Backfill: расширяем смену до 22:00, чтобы слоты были видны весь рабочий день филиала.
            // Это безопасно: для старых демо-данных смена была 10:00-20:00, что обрезало слоты до 19:00 для услуг по 60 минут.
            var oldEnd = new TimeOnly(20, 0);
            var newEnd = new TimeOnly(22, 0);
            var oldShifts = await db.WorkSchedules
                .Where(w => w.ScheduleType == ScheduleType.Work && w.EndTime == oldEnd && w.StartTime == new TimeOnly(10, 0))
                .ToListAsync(ct);
            if (oldShifts.Count > 0)
            {
                foreach (var s in oldShifts) s.EndTime = newEnd;
                await db.SaveChangesAsync(ct);
                logger.LogInformation("SeedDevData: extended {Count} legacy 10:00-20:00 shifts to 22:00", oldShifts.Count);
            }
            else
            {
                logger.LogInformation("SeedDevData: schedules already present, skipping");
            }
            return;
        }

        var masters = await db.Masters
            .Where(m => m.IsActive)
            .Select(m => new { m.MasterId, m.BranchId })
            .ToListAsync(ct);
        if (masters.Count == 0) return;

        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        // Seed 14 days starting 7 days ago so the demo also covers history.
        var start = today.AddDays(-7);
        var end = today.AddDays(14);

        // Standard shift 10:00..22:00 (= branch hours) with lunch 14:00..15:00, days off Sundays.
        // Совпадает с часами работы филиала, чтобы клиент видел слоты до позднего вечера.
        var shiftStart = new TimeOnly(10, 0);
        var shiftEnd = new TimeOnly(22, 0);
        var lunchStart = new TimeOnly(14, 0);
        var lunchEnd = new TimeOnly(15, 0);

        int added = 0;
        foreach (var m in masters)
        {
            for (var d = start; d <= end; d = d.AddDays(1))
            {
                if (d.DayOfWeek == DayOfWeek.Sunday)
                {
                    db.WorkSchedules.Add(new WorkSchedule
                    {
                        MasterId = m.MasterId,
                        BranchId = m.BranchId,
                        WorkDate = d,
                        StartTime = new TimeOnly(0, 0),
                        EndTime = new TimeOnly(23, 59),
                        ScheduleType = ScheduleType.DayOff,
                    });
                    added++;
                    continue;
                }

                db.WorkSchedules.Add(new WorkSchedule
                {
                    MasterId = m.MasterId,
                    BranchId = m.BranchId,
                    WorkDate = d,
                    StartTime = shiftStart,
                    EndTime = shiftEnd,
                    ScheduleType = ScheduleType.Work,
                });
                db.WorkSchedules.Add(new WorkSchedule
                {
                    MasterId = m.MasterId,
                    BranchId = m.BranchId,
                    WorkDate = d,
                    StartTime = lunchStart,
                    EndTime = lunchEnd,
                    ScheduleType = ScheduleType.Lunch,
                });
                added += 2;
            }
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("SeedDevData: seeded {Count} WorkSchedule rows", added);
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
        string position, DateOnly hireDate,
        int[]? serviceIds = null)
    {
        var user = Add(db, hash, now, masterRoleId, branchId, login, lastName, firstName, middleName, phone,
            emailConfirmed: true);
        var master = new Master
        {
            Persona = user.Persona,
            BranchId = branchId,
            Position = position,
            HireDate = hireDate,
            IsActive = true,
        };
        db.Masters.Add(master);

        if (serviceIds is not null)
        {
            foreach (var sid in serviceIds)
            {
                db.MasterServices.Add(new MasterService { Master = master, ServiceId = sid });
            }
        }
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
