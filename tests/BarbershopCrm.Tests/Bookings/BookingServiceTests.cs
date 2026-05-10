using BarbershopCrm.Domain.Entities;
using BarbershopCrm.Domain.Enums;
using BarbershopCrm.Infrastructure.Bookings;
using BarbershopCrm.Infrastructure.Data;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BarbershopCrm.Tests.Bookings;

public class BookingServiceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    private int _branchId;
    private int _serviceId;
    private int _masterId;
    private int _clientId;
    private int _clientUserId;
    private int _otherClientId;
    private int _adminUserId;
    private int _ownerUserId;
    private int _masterUserId;

    public BookingServiceTests()
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

    public Task DisposeAsync() { _connection.Dispose(); return Task.CompletedTask; }

    private async Task SeedAsync(AppDbContext db)
    {
        // Use seed branches/services from HasData.
        var branch = await db.Branches.OrderBy(b => b.BranchId).FirstAsync();
        var service = await db.Services.OrderBy(s => s.ServiceId).FirstAsync(s => s.DurationMinutes == 60);
        _branchId = branch.BranchId;
        _serviceId = service.ServiceId;

        // Create master persona + user.
        var clientRoleId = (await db.Roles.FirstAsync(r => r.Code == RoleCode.Client)).RoleId;
        var masterRoleId = (await db.Roles.FirstAsync(r => r.Code == RoleCode.Master)).RoleId;
        var adminRoleId = (await db.Roles.FirstAsync(r => r.Code == RoleCode.Admin)).RoleId;
        var ownerRoleId = (await db.Roles.FirstAsync(r => r.Code == RoleCode.Owner)).RoleId;

        var pm = new Persona { LastName = "Кузнецов", FirstName = "Артём", Phone = "+79180000020" };
        db.Persona.Add(pm);
        await db.SaveChangesAsync();
        var um = new User { PersonaId = pm.PersonaId, RoleId = masterRoleId, Login = "m@thq.ru", PasswordHash = "x", PasswordSalt = "x" };
        db.Users.Add(um);
        await db.SaveChangesAsync();
        _masterUserId = um.UserId;

        var master = new Master { PersonaId = pm.PersonaId, BranchId = _branchId, Position = "Барбер", HireDate = new DateOnly(2024, 1, 1), IsActive = true };
        db.Masters.Add(master);
        await db.SaveChangesAsync();
        _masterId = master.MasterId;
        db.MasterServices.Add(new MasterService { MasterId = _masterId, ServiceId = _serviceId });

        // Client + user.
        var pc = new Persona { LastName = "Иванов", FirstName = "Михаил", Phone = "+79180000030" };
        db.Persona.Add(pc);
        await db.SaveChangesAsync();
        var uc = new User { PersonaId = pc.PersonaId, RoleId = clientRoleId, Login = "c@thq.ru", PasswordHash = "x", PasswordSalt = "x" };
        db.Users.Add(uc);
        await db.SaveChangesAsync();
        _clientUserId = uc.UserId;
        var c = new Client { PersonaId = pc.PersonaId };
        db.Clients.Add(c);

        // Other client (does not own first client's bookings).
        var pc2 = new Persona { LastName = "Сидоров", FirstName = "Олег", Phone = "+79180000031" };
        db.Persona.Add(pc2);
        await db.SaveChangesAsync();
        var c2 = new Client { PersonaId = pc2.PersonaId };
        db.Clients.Add(c2);
        await db.SaveChangesAsync();
        _clientId = c.ClientId;
        _otherClientId = c2.ClientId;

        // Admin user (same branch).
        var pa = new Persona { LastName = "Адмов", FirstName = "Иван", Phone = "+79180000040" };
        db.Persona.Add(pa);
        await db.SaveChangesAsync();
        var ua = new User { PersonaId = pa.PersonaId, RoleId = adminRoleId, BranchId = _branchId, Login = "a@thq.ru", PasswordHash = "x", PasswordSalt = "x" };
        db.Users.Add(ua);
        await db.SaveChangesAsync();
        _adminUserId = ua.UserId;

        // Owner user.
        var po = new Persona { LastName = "Влад", FirstName = "Имир", Phone = "+79180000050" };
        db.Persona.Add(po);
        await db.SaveChangesAsync();
        var uo = new User { PersonaId = po.PersonaId, RoleId = ownerRoleId, Login = "o@thq.ru", PasswordHash = "x", PasswordSalt = "x" };
        db.Users.Add(uo);
        await db.SaveChangesAsync();
        _ownerUserId = uo.UserId;

        // Work schedule for tomorrow 10–22 (matches branch 10–22) with lunch 14–15.
        var date = DateOnly.FromDateTime(NextWeekday().Date);
        db.WorkSchedules.Add(new WorkSchedule { MasterId = _masterId, BranchId = _branchId, WorkDate = date, StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(22, 0), ScheduleType = ScheduleType.Work });
        db.WorkSchedules.Add(new WorkSchedule { MasterId = _masterId, BranchId = _branchId, WorkDate = date, StartTime = new TimeOnly(14, 0), EndTime = new TimeOnly(15, 0), ScheduleType = ScheduleType.Lunch });
        await db.SaveChangesAsync();
    }

    private static DateTime NextWeekday()
    {
        var d = DateTime.Today.AddDays(1);
        while (d.DayOfWeek == DayOfWeek.Sunday) d = d.AddDays(1);
        return d;
    }

    private BookingService NewService(AppDbContext db) =>
        new BookingService(db, Options.Create(new BookingOptions { SlotIntervalMinutes = 15, CancelCutoffHours = 2 }));

    [Fact]
    public async Task Create_ValidSlot_Succeeds()
    {
        using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var start = NextWeekday().AddHours(11);
        var r = await svc.CreateAsync(new CreateBookingCommand(_clientId, _branchId, _serviceId, _masterId, start, BookingSource.Online), default);
        r.Success.Should().BeTrue();
        r.BookingId.Should().NotBeNull();

        using var db2 = new AppDbContext(_options);
        var bk = await db2.Bookings.FirstAsync(b => b.BookingId == r.BookingId);
        bk.Status.Should().Be(BookingStatus.Created);
        bk.PriceSnapshot.Should().Be(1500m);
        bk.DurationMinutes.Should().Be(60);
    }

    [Fact]
    public async Task Create_DoubleBooking_FailsWithSlotTaken()
    {
        using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var start = NextWeekday().AddHours(11);
        var first = await svc.CreateAsync(new CreateBookingCommand(_clientId, _branchId, _serviceId, _masterId, start, BookingSource.Online), default);
        first.Success.Should().BeTrue();

        var second = await svc.CreateAsync(new CreateBookingCommand(_otherClientId, _branchId, _serviceId, _masterId, start, BookingSource.Online), default);
        second.Success.Should().BeFalse();
        second.ErrorCode.Should().Be(BookingErrorCode.SlotTaken);
    }

    [Fact]
    public async Task Create_OverlappingTime_FailsWithSlotTaken()
    {
        using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var start = NextWeekday().AddHours(11);
        var r1 = await svc.CreateAsync(new CreateBookingCommand(_clientId, _branchId, _serviceId, _masterId, start, BookingSource.Online), default);
        r1.Success.Should().BeTrue();

        // 11:30 overlaps with 11:00–12:00.
        var overlapStart = start.AddMinutes(30);
        var r2 = await svc.CreateAsync(new CreateBookingCommand(_otherClientId, _branchId, _serviceId, _masterId, overlapStart, BookingSource.Online), default);
        r2.Success.Should().BeFalse();
        r2.ErrorCode.Should().Be(BookingErrorCode.SlotTaken);
    }

    [Fact]
    public async Task Create_DuringLunch_FailsOutsideSchedule()
    {
        using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var start = NextWeekday().AddHours(14);
        var r = await svc.CreateAsync(new CreateBookingCommand(_clientId, _branchId, _serviceId, _masterId, start, BookingSource.Online), default);
        r.Success.Should().BeFalse();
        r.ErrorCode.Should().Be(BookingErrorCode.OutsideWorkSchedule);
    }

    [Fact]
    public async Task Create_OutsideWorkingHours_Fails()
    {
        using var db = new AppDbContext(_options);
        var svc = NewService(db);
        // Branch closes at 22:00; service is 60 min, so 21:30 would end at 22:30.
        var start = NextWeekday().AddHours(21).AddMinutes(30);
        var r = await svc.CreateAsync(new CreateBookingCommand(_clientId, _branchId, _serviceId, _masterId, start, BookingSource.Online), default);
        r.Success.Should().BeFalse();
        r.ErrorCode.Should().Be(BookingErrorCode.OutsideWorkingHours);
    }

    [Fact]
    public async Task Create_InThePast_Fails()
    {
        using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var r = await svc.CreateAsync(new CreateBookingCommand(_clientId, _branchId, _serviceId, _masterId, DateTime.UtcNow.AddDays(-1), BookingSource.Online), default);
        r.Success.Should().BeFalse();
        r.ErrorCode.Should().Be(BookingErrorCode.SlotInPast);
    }

    [Fact]
    public async Task Create_SlotNotMultipleOfStep_Fails()
    {
        using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var start = NextWeekday().AddHours(11).AddMinutes(7);
        var r = await svc.CreateAsync(new CreateBookingCommand(_clientId, _branchId, _serviceId, _masterId, start, BookingSource.Online), default);
        r.Success.Should().BeFalse();
        r.ErrorCode.Should().Be(BookingErrorCode.SlotInvalid);
    }

    [Fact]
    public async Task Cancel_ByClientWithinCutoff_Fails()
    {
        using var db = new AppDbContext(_options);
        var svc = NewService(db);
        // Direct insert: too-soon booking (1 hour from now).
        var bk = new Booking
        {
            ClientId = _clientId, BranchId = _branchId, ServiceId = _serviceId, MasterId = _masterId,
            StartDateTime = DateTime.UtcNow.AddHours(1), DurationMinutes = 60, PriceSnapshot = 1500m,
            Status = BookingStatus.Confirmed, Source = BookingSource.Online,
        };
        db.Bookings.Add(bk);
        await db.SaveChangesAsync();

        var r = await svc.CancelAsync(bk.BookingId, _clientUserId, RoleCode.Client, "test", default);
        r.Success.Should().BeFalse();
        r.ErrorCode.Should().Be(BookingErrorCode.CancelTooLate);
    }

    [Fact]
    public async Task Cancel_ByClient_OtherClientForbidden()
    {
        using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var bk = new Booking
        {
            ClientId = _clientId, BranchId = _branchId, ServiceId = _serviceId, MasterId = _masterId,
            StartDateTime = DateTime.UtcNow.AddDays(2), DurationMinutes = 60, PriceSnapshot = 1500m,
            Status = BookingStatus.Confirmed, Source = BookingSource.Online,
        };
        db.Bookings.Add(bk);
        await db.SaveChangesAsync();

        // Use admin's user id with Client role to simulate "other user" cancellation.
        var r = await svc.CancelAsync(bk.BookingId, _adminUserId, RoleCode.Client, null, default);
        r.Success.Should().BeFalse();
        r.ErrorCode.Should().Be(BookingErrorCode.Unauthorized);
    }

    [Fact]
    public async Task Cancel_ByAdmin_Succeeds()
    {
        using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var bk = new Booking
        {
            ClientId = _clientId, BranchId = _branchId, ServiceId = _serviceId, MasterId = _masterId,
            StartDateTime = DateTime.UtcNow.AddHours(1), DurationMinutes = 60, PriceSnapshot = 1500m,
            Status = BookingStatus.Confirmed, Source = BookingSource.Online,
        };
        db.Bookings.Add(bk);
        await db.SaveChangesAsync();

        var r = await svc.CancelAsync(bk.BookingId, _adminUserId, RoleCode.Admin, "no-show by phone", default);
        r.Success.Should().BeTrue();
        var bk2 = await db.Bookings.AsNoTracking().FirstAsync(b => b.BookingId == bk.BookingId);
        bk2.Status.Should().Be(BookingStatus.Cancelled);
    }

    [Fact]
    public async Task Confirm_ByMaster_Succeeds()
    {
        using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var bk = new Booking
        {
            ClientId = _clientId, BranchId = _branchId, ServiceId = _serviceId, MasterId = _masterId,
            StartDateTime = NextWeekday().AddHours(11), DurationMinutes = 60, PriceSnapshot = 1500m,
            Status = BookingStatus.Created, Source = BookingSource.Online,
        };
        db.Bookings.Add(bk);
        await db.SaveChangesAsync();

        var r = await svc.ConfirmAsync(bk.BookingId, _masterUserId, RoleCode.Master, default);
        r.Success.Should().BeTrue();
        var saved = await db.Bookings.AsNoTracking().FirstAsync(b => b.BookingId == bk.BookingId);
        saved.Status.Should().Be(BookingStatus.Confirmed);
    }

    [Fact]
    public async Task Confirm_ByDifferentMaster_Forbidden()
    {
        using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var bk = new Booking
        {
            ClientId = _clientId, BranchId = _branchId, ServiceId = _serviceId, MasterId = _masterId,
            StartDateTime = NextWeekday().AddHours(11), DurationMinutes = 60, PriceSnapshot = 1500m,
            Status = BookingStatus.Created, Source = BookingSource.Online,
        };
        db.Bookings.Add(bk);
        await db.SaveChangesAsync();

        // Use admin's user id with Master role -> not the master persona.
        var r = await svc.ConfirmAsync(bk.BookingId, _adminUserId, RoleCode.Master, default);
        r.Success.Should().BeFalse();
        r.ErrorCode.Should().Be(BookingErrorCode.Unauthorized);
    }

    [Fact]
    public async Task Complete_CreatesVisit()
    {
        using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var bk = new Booking
        {
            ClientId = _clientId, BranchId = _branchId, ServiceId = _serviceId, MasterId = _masterId,
            StartDateTime = NextWeekday().AddHours(11), DurationMinutes = 60, PriceSnapshot = 1500m,
            Status = BookingStatus.Confirmed, Source = BookingSource.Online,
        };
        db.Bookings.Add(bk);
        await db.SaveChangesAsync();

        var r = await svc.CompleteAsync(new CompleteBookingCommand(bk.BookingId, 1700m, "ok"), _ownerUserId, RoleCode.Owner, default);
        r.Success.Should().BeTrue();
        var saved = await db.Bookings.Include(b => b.Visit).AsNoTracking().FirstAsync(b => b.BookingId == bk.BookingId);
        saved.Status.Should().Be(BookingStatus.Completed);
        saved.Visit.Should().NotBeNull();
        saved.Visit!.TotalAmount.Should().Be(1700m);
        saved.Visit.MasterNotes.Should().Be("ok");
    }

    [Fact]
    public async Task NoShow_TransitionsCorrectly()
    {
        using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var bk = new Booking
        {
            ClientId = _clientId, BranchId = _branchId, ServiceId = _serviceId, MasterId = _masterId,
            StartDateTime = NextWeekday().AddHours(11), DurationMinutes = 60, PriceSnapshot = 1500m,
            Status = BookingStatus.Confirmed, Source = BookingSource.Online,
        };
        db.Bookings.Add(bk);
        await db.SaveChangesAsync();

        var r = await svc.NoShowAsync(bk.BookingId, _ownerUserId, RoleCode.Owner, default);
        r.Success.Should().BeTrue();
        var saved = await db.Bookings.AsNoTracking().FirstAsync(b => b.BookingId == bk.BookingId);
        saved.Status.Should().Be(BookingStatus.NoShow);
    }

    [Fact]
    public async Task Cancel_AfterCompleted_Fails()
    {
        using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var bk = new Booking
        {
            ClientId = _clientId, BranchId = _branchId, ServiceId = _serviceId, MasterId = _masterId,
            StartDateTime = DateTime.UtcNow.AddHours(-2), DurationMinutes = 60, PriceSnapshot = 1500m,
            Status = BookingStatus.Completed, Source = BookingSource.Online,
        };
        db.Bookings.Add(bk);
        await db.SaveChangesAsync();
        var r = await svc.CancelAsync(bk.BookingId, _ownerUserId, RoleCode.Owner, null, default);
        r.Success.Should().BeFalse();
        r.ErrorCode.Should().Be(BookingErrorCode.InvalidStatusTransition);
    }

    [Fact]
    public async Task Create_MasterCannotDoService_Fails()
    {
        using var db = new AppDbContext(_options);
        // Add a master without service link.
        var pm = new Persona { LastName = "Иной", FirstName = "Мастер", Phone = "+79180000022" };
        db.Persona.Add(pm);
        await db.SaveChangesAsync();
        var m2 = new Master { PersonaId = pm.PersonaId, BranchId = _branchId, Position = "Барбер", HireDate = new DateOnly(2024, 1, 1), IsActive = true };
        db.Masters.Add(m2);
        await db.SaveChangesAsync();
        var date = DateOnly.FromDateTime(NextWeekday().Date);
        db.WorkSchedules.Add(new WorkSchedule { MasterId = m2.MasterId, BranchId = _branchId, WorkDate = date, StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(20, 0), ScheduleType = ScheduleType.Work });
        await db.SaveChangesAsync();

        var svc = NewService(db);
        var r = await svc.CreateAsync(new CreateBookingCommand(_clientId, _branchId, _serviceId, m2.MasterId, NextWeekday().AddHours(12), BookingSource.Online), default);
        r.Success.Should().BeFalse();
        r.ErrorCode.Should().Be(BookingErrorCode.MasterCannotDoService);
    }
}
