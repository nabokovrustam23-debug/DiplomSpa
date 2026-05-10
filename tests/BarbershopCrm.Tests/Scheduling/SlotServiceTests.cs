using BarbershopCrm.Domain.Entities;
using BarbershopCrm.Domain.Enums;
using BarbershopCrm.Infrastructure.Data;
using BarbershopCrm.Infrastructure.Scheduling;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BarbershopCrm.Tests.Scheduling;

public class SlotServiceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public SlotServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:;Cache=Shared");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
    }

    public async Task InitializeAsync()
    {
        using var db = new AppDbContext(_options);
        await db.Database.EnsureCreatedAsync();
        await SeedAsync(db);
    }

    public Task DisposeAsync()
    {
        _connection.Dispose();
        return Task.CompletedTask;
    }

    private static async Task SeedAsync(AppDbContext db)
    {
        var branch = new Branch
        {
            Name = "Центр", Address = "ул. Красная, 1",
            OpeningTime = new TimeOnly(10, 0), ClosingTime = new TimeOnly(20, 0),
            IsActive = true,
        };
        db.Branches.Add(branch);

        var service = new Service
        {
            Name = "Стрижка", DurationMinutes = 60, Price = 1500m, IsActive = true,
        };
        db.Services.Add(service);
        await db.SaveChangesAsync();

        var p1 = new Persona { LastName = "Кузнецов", FirstName = "Артём", Phone = "+79180000020" };
        var p2 = new Persona { LastName = "Морозов",  FirstName = "Денис",  Phone = "+79180000021" };
        db.Persona.AddRange(p1, p2);
        await db.SaveChangesAsync();

        var m1 = new Master { PersonaId = p1.PersonaId, BranchId = branch.BranchId, Position = "Барбер", HireDate = new DateOnly(2024,1,1), IsActive = true };
        var m2 = new Master { PersonaId = p2.PersonaId, BranchId = branch.BranchId, Position = "Барбер", HireDate = new DateOnly(2024,1,1), IsActive = true };
        db.Masters.AddRange(m1, m2);
        await db.SaveChangesAsync();

        db.MasterServices.AddRange(
            new MasterService { MasterId = m1.MasterId, ServiceId = service.ServiceId },
            new MasterService { MasterId = m2.MasterId, ServiceId = service.ServiceId });

        var date = new DateOnly(2026, 5, 11);
        foreach (var mid in new[] { m1.MasterId, m2.MasterId })
        {
            db.WorkSchedules.Add(new WorkSchedule { MasterId = mid, BranchId = branch.BranchId, WorkDate = date, StartTime = new TimeOnly(10,0), EndTime = new TimeOnly(20,0), ScheduleType = ScheduleType.Work });
            db.WorkSchedules.Add(new WorkSchedule { MasterId = mid, BranchId = branch.BranchId, WorkDate = date, StartTime = new TimeOnly(14,0), EndTime = new TimeOnly(15,0), ScheduleType = ScheduleType.Lunch });
        }

        // Master 1 vacation on 05-12.
        db.WorkSchedules.Add(new WorkSchedule { MasterId = m1.MasterId, BranchId = branch.BranchId, WorkDate = date.AddDays(1), StartTime = new TimeOnly(0,0), EndTime = new TimeOnly(23,59), ScheduleType = ScheduleType.Vacation });

        await db.SaveChangesAsync();
    }

    private async Task<(int branchId, int serviceId, int m1, int m2)> ResolveIdsAsync()
    {
        using var db = new AppDbContext(_options);
        // The DB ships with HasData seed (branches/services), so resolve by name.
        var b = await db.Branches.FirstAsync(x => x.Name == "Центр");
        var s = await db.Services.FirstAsync(x => x.Name == "Стрижка");
        var ms = await db.Masters.OrderBy(m => m.MasterId)
            .Where(m => m.BranchId == b.BranchId)
            .ToListAsync();
        return (b.BranchId, s.ServiceId, ms[0].MasterId, ms[1].MasterId);
    }

    [Fact]
    public async Task FreeSlots_TwoMastersBothWorkingNoBookings_ReturnsCombinedSorted()
    {
        var (branchId, serviceId, _, _) = await ResolveIdsAsync();
        using var db = new AppDbContext(_options);

        var svc = new SlotService(db);

        var slots = await svc.GetFreeSlotsAsync(branchId, serviceId, new DateOnly(2026, 5, 11), masterId: null, default);

        slots.Should().NotBeEmpty();
        // sorted by start, then name (Кузнецов < Морозов alphabetically in ru)
        slots.Should().BeInAscendingOrder(s => s.StartDateTime);
        slots.First().StartDateTime.TimeOfDay.Should().Be(TimeSpan.FromHours(10));
        slots.Last().StartDateTime.TimeOfDay.Should().Be(TimeSpan.FromHours(19));
    }

    [Fact]
    public async Task FreeSlots_VacationMasterFiltered_ReturnsZero()
    {
        var (branchId, serviceId, m1, _) = await ResolveIdsAsync();
        using var db = new AppDbContext(_options);
        var svc = new SlotService(db);

        var slots = await svc.GetFreeSlotsAsync(branchId, serviceId, new DateOnly(2026, 5, 12), masterId: m1, default);

        slots.Should().BeEmpty();
    }

    [Fact]
    public async Task FreeSlots_BookingBlocksOverlappingSlots()
    {
        var (branchId, serviceId, m1, _) = await ResolveIdsAsync();
        using var db = new AppDbContext(_options);

        db.Persona.Add(new Persona { LastName = "Иванов", FirstName = "Сергей", Phone = "+79180009999" });
        await db.SaveChangesAsync();
        var personaId = (await db.Persona.OrderByDescending(p => p.PersonaId).FirstAsync()).PersonaId;
        db.Clients.Add(new Client { PersonaId = personaId });
        await db.SaveChangesAsync();
        var clientId = (await db.Clients.OrderByDescending(c => c.ClientId).FirstAsync()).ClientId;

        db.Bookings.Add(new Booking
        {
            ClientId = clientId, MasterId = m1, ServiceId = serviceId, BranchId = branchId,
            StartDateTime = new DateTime(2026, 5, 11, 11, 0, 0),
            DurationMinutes = 60, PriceSnapshot = 1500m, Status = BookingStatus.Confirmed,
            Source = "Admin", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var svc = new SlotService(db);
        var slots = await svc.GetFreeSlotsAsync(branchId, serviceId, new DateOnly(2026, 5, 11), masterId: m1, default);
        var times = slots.Select(s => s.StartDateTime.ToString("HH:mm")).ToList();

        times.Should().NotContain(new[] { "10:15", "10:30", "10:45", "11:00", "11:15", "11:30", "11:45" });
        times.Should().Contain("10:00");
        times.Should().Contain("12:00");
    }

    [Fact]
    public async Task FreeSlots_InactiveBranchOrService_ReturnsEmpty()
    {
        var (branchId, serviceId, _, _) = await ResolveIdsAsync();
        using var db = new AppDbContext(_options);
        var svc = new SlotService(db);

        var byBranch = await svc.GetFreeSlotsAsync(99999, serviceId, new DateOnly(2026, 5, 11), null, default);
        byBranch.Should().BeEmpty();

        var byService = await svc.GetFreeSlotsAsync(branchId, 99999, new DateOnly(2026, 5, 11), null, default);
        byService.Should().BeEmpty();
    }
}
