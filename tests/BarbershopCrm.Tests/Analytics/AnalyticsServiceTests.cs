using BarbershopCrm.Domain.Entities;
using BarbershopCrm.Domain.Enums;
using BarbershopCrm.Infrastructure.Analytics;
using BarbershopCrm.Infrastructure.Data;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BarbershopCrm.Tests.Analytics;

public class AnalyticsServiceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    private int _branchA;
    private int _branchB;
    private int _serviceShort;   // 30 min, 500
    private int _serviceLong;    // 60 min, 1000
    private int _masterA1;
    private int _masterA2;
    private int _masterB1;
    private int _client1;
    private int _client2;
    private int _client3;

    public AnalyticsServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:;Cache=Shared");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
    }

    public async Task InitializeAsync()
    {
        using var db = new AppDbContext(_options);
        await db.Database.EnsureCreatedAsync();

        // Use one of the seeded branches as A; create branch B explicitly so we can test multi-branch.
        var seededBranch = await db.Branches.OrderBy(b => b.BranchId).FirstAsync();
        _branchA = seededBranch.BranchId;

        var b2 = new Branch
        {
            Name = "Тестовый B",
            Address = "Краснодар, ул. Тестовая, 2",
            Phone = "+78610000002",
            OpeningTime = new TimeOnly(10, 0),
            ClosingTime = new TimeOnly(20, 0),
            IsActive = true,
        };
        db.Branches.Add(b2);
        await db.SaveChangesAsync();
        _branchB = b2.BranchId;

        // Seeded services have known durations; create custom test services to control prices precisely.
        var sShort = new Service { Name = "Тест короткий", DurationMinutes = 30, Price = 500m, IsActive = true };
        var sLong = new Service { Name = "Тест длинный", DurationMinutes = 60, Price = 1000m, IsActive = true };
        db.Services.AddRange(sShort, sLong);
        await db.SaveChangesAsync();
        _serviceShort = sShort.ServiceId;
        _serviceLong = sLong.ServiceId;

        var masterRoleId = (await db.Roles.FirstAsync(r => r.Code == RoleCode.Master)).RoleId;
        var clientRoleId = (await db.Roles.FirstAsync(r => r.Code == RoleCode.Client)).RoleId;

        _masterA1 = await CreateMasterAsync(db, _branchA, "Иванов", "Алексей", "+79180001001");
        _masterA2 = await CreateMasterAsync(db, _branchA, "Петров", "Борис", "+79180001002");
        _masterB1 = await CreateMasterAsync(db, _branchB, "Сидоров", "Виктор", "+79180001003");

        _client1 = await CreateClientAsync(db, "Клиент", "Один", "+79180002001");
        _client2 = await CreateClientAsync(db, "Клиент", "Два", "+79180002002");
        _client3 = await CreateClientAsync(db, "Клиент", "Три", "+79180002003");

        // Master-service mapping (everyone can do everything).
        foreach (var mId in new[] { _masterA1, _masterA2, _masterB1 })
        {
            db.MasterServices.Add(new MasterService { MasterId = mId, ServiceId = _serviceShort });
            db.MasterServices.Add(new MasterService { MasterId = mId, ServiceId = _serviceLong });
        }
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() { _connection.Dispose(); return Task.CompletedTask; }

    private static async Task<int> CreateMasterAsync(AppDbContext db, int branchId, string last, string first, string phone)
    {
        var p = new Persona { LastName = last, FirstName = first, Phone = phone };
        db.Persona.Add(p);
        await db.SaveChangesAsync();
        var m = new Master { PersonaId = p.PersonaId, BranchId = branchId, Position = "Тест", HireDate = DateOnly.FromDateTime(DateTime.UtcNow), IsActive = true };
        db.Masters.Add(m);
        await db.SaveChangesAsync();
        return m.MasterId;
    }

    private static async Task<int> CreateClientAsync(AppDbContext db, string last, string first, string phone)
    {
        var p = new Persona { LastName = last, FirstName = first, Phone = phone };
        db.Persona.Add(p);
        await db.SaveChangesAsync();
        var c = new Client { PersonaId = p.PersonaId, CreatedAt = DateTime.UtcNow };
        db.Clients.Add(c);
        await db.SaveChangesAsync();
        return c.ClientId;
    }

    [Fact]
    public async Task EmptyDatabase_ReturnsZeroes()
    {
        using var db = new AppDbContext(_options);
        var svc = new AnalyticsService(db);
        var from = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var to = from.AddDays(7);
        var snap = await svc.GetDashboardAsync(null, from, to);

        snap.TotalBookings.Should().Be(0);
        snap.Revenue.Should().Be(0m);
        snap.AverageTicket.Should().Be(0m);
        snap.CompletedVisits.Should().Be(0);
        snap.RepeatClientsRate.Should().Be(0m);
        snap.ClientsWithAtLeastOneCompletedVisit.Should().Be(0);
        snap.ByStatus.Total.Should().Be(0);
        snap.Utilization.Should().BeEmpty();
        snap.TopServices.Should().BeEmpty();
    }

    [Fact]
    public async Task BookingsByStatus_GroupedCorrectly()
    {
        var day = DateTime.UtcNow.Date.AddDays(-3);
        using (var db = new AppDbContext(_options))
        {
            db.Bookings.AddRange(
                MakeBooking(_branchA, _masterA1, _serviceShort, _client1, day.AddHours(10), 30, 500m, BookingStatus.Created),
                MakeBooking(_branchA, _masterA1, _serviceShort, _client2, day.AddHours(11), 30, 500m, BookingStatus.Confirmed),
                MakeBooking(_branchA, _masterA1, _serviceLong, _client3, day.AddHours(12), 60, 1000m, BookingStatus.Completed),
                MakeBooking(_branchA, _masterA2, _serviceShort, _client1, day.AddHours(13), 30, 500m, BookingStatus.Cancelled),
                MakeBooking(_branchA, _masterA2, _serviceShort, _client2, day.AddHours(14), 30, 500m, BookingStatus.NoShow));
            await db.SaveChangesAsync();
        }
        using (var db = new AppDbContext(_options))
        {
            var svc = new AnalyticsService(db);
            var snap = await svc.GetDashboardAsync(_branchA, DateOnly.FromDateTime(day), DateOnly.FromDateTime(day));
            snap.TotalBookings.Should().Be(5);
            snap.ByStatus.Created.Should().Be(1);
            snap.ByStatus.Confirmed.Should().Be(1);
            snap.ByStatus.Completed.Should().Be(1);
            snap.ByStatus.Cancelled.Should().Be(1);
            snap.ByStatus.NoShow.Should().Be(1);
        }
    }

    [Fact]
    public async Task RevenueAndAverageTicket_CountVisitsOnly()
    {
        var day = DateTime.UtcNow.Date.AddDays(-2);
        using (var db = new AppDbContext(_options))
        {
            var b1 = MakeBooking(_branchA, _masterA1, _serviceLong, _client1, day.AddHours(10), 60, 1000m, BookingStatus.Completed);
            var b2 = MakeBooking(_branchA, _masterA1, _serviceLong, _client2, day.AddHours(11), 60, 1000m, BookingStatus.Completed);
            var b3 = MakeBooking(_branchA, _masterA1, _serviceShort, _client3, day.AddHours(12), 30, 500m, BookingStatus.Confirmed);
            db.Bookings.AddRange(b1, b2, b3);
            await db.SaveChangesAsync();
            db.Visits.AddRange(
                new Visit { BookingId = b1.BookingId, TotalAmount = 1200m, CompletedAt = day.AddHours(11) },
                new Visit { BookingId = b2.BookingId, TotalAmount = 800m,  CompletedAt = day.AddHours(12) });
            await db.SaveChangesAsync();
        }
        using (var db = new AppDbContext(_options))
        {
            var svc = new AnalyticsService(db);
            var snap = await svc.GetDashboardAsync(_branchA, DateOnly.FromDateTime(day), DateOnly.FromDateTime(day));
            snap.CompletedVisits.Should().Be(2);
            snap.Revenue.Should().Be(2000m);
            snap.AverageTicket.Should().Be(1000m);
        }
    }

    [Fact]
    public async Task RepeatClientsRate_CountsClientsWithMultipleCompletedVisits()
    {
        var day = DateTime.UtcNow.Date.AddDays(-5);
        using (var db = new AppDbContext(_options))
        {
            // Client1: 2 visits (repeat). Client2: 1 visit. Client3: 1 visit.
            var b1a = MakeBooking(_branchA, _masterA1, _serviceShort, _client1, day.AddHours(9), 30, 500m, BookingStatus.Completed);
            var b1b = MakeBooking(_branchA, _masterA1, _serviceShort, _client1, day.AddHours(10), 30, 500m, BookingStatus.Completed);
            var b2 = MakeBooking(_branchA, _masterA1, _serviceShort, _client2, day.AddHours(11), 30, 500m, BookingStatus.Completed);
            var b3 = MakeBooking(_branchA, _masterA1, _serviceShort, _client3, day.AddHours(12), 30, 500m, BookingStatus.Completed);
            db.Bookings.AddRange(b1a, b1b, b2, b3);
            await db.SaveChangesAsync();
            db.Visits.AddRange(
                new Visit { BookingId = b1a.BookingId, TotalAmount = 500m, CompletedAt = day.AddHours(9.5) },
                new Visit { BookingId = b1b.BookingId, TotalAmount = 500m, CompletedAt = day.AddHours(10.5) },
                new Visit { BookingId = b2.BookingId,  TotalAmount = 500m, CompletedAt = day.AddHours(11.5) },
                new Visit { BookingId = b3.BookingId,  TotalAmount = 500m, CompletedAt = day.AddHours(12.5) });
            await db.SaveChangesAsync();
        }
        using (var db = new AppDbContext(_options))
        {
            var svc = new AnalyticsService(db);
            var snap = await svc.GetDashboardAsync(_branchA, DateOnly.FromDateTime(day), DateOnly.FromDateTime(day));
            snap.ClientsWithAtLeastOneCompletedVisit.Should().Be(3);
            snap.RepeatClients.Should().Be(1);
            snap.RepeatClientsRate.Should().BeApproximately(0.3333m, 0.0001m);
        }
    }

    [Fact]
    public async Task DateRangeFilter_ExcludesOutOfRangeBookings()
    {
        var inDay = DateTime.UtcNow.Date.AddDays(-10);
        var outDay = DateTime.UtcNow.Date.AddDays(-30);
        using (var db = new AppDbContext(_options))
        {
            db.Bookings.AddRange(
                MakeBooking(_branchA, _masterA1, _serviceShort, _client1, inDay.AddHours(10), 30, 500m, BookingStatus.Completed),
                MakeBooking(_branchA, _masterA1, _serviceShort, _client2, outDay.AddHours(10), 30, 500m, BookingStatus.Completed));
            await db.SaveChangesAsync();
        }
        using (var db = new AppDbContext(_options))
        {
            var svc = new AnalyticsService(db);
            var snap = await svc.GetDashboardAsync(
                _branchA, DateOnly.FromDateTime(inDay), DateOnly.FromDateTime(inDay));
            snap.TotalBookings.Should().Be(1);
        }
    }

    [Fact]
    public async Task BranchFilter_LimitsScope()
    {
        var day = DateTime.UtcNow.Date.AddDays(-1);
        using (var db = new AppDbContext(_options))
        {
            db.Bookings.AddRange(
                MakeBooking(_branchA, _masterA1, _serviceShort, _client1, day.AddHours(10), 30, 500m, BookingStatus.Completed),
                MakeBooking(_branchB, _masterB1, _serviceShort, _client2, day.AddHours(11), 30, 500m, BookingStatus.Completed));
            await db.SaveChangesAsync();
        }
        using (var db = new AppDbContext(_options))
        {
            var svc = new AnalyticsService(db);
            var snapA = await svc.GetDashboardAsync(_branchA, DateOnly.FromDateTime(day), DateOnly.FromDateTime(day));
            var snapAll = await svc.GetDashboardAsync(null, DateOnly.FromDateTime(day), DateOnly.FromDateTime(day));
            snapA.TotalBookings.Should().Be(1);
            snapAll.TotalBookings.Should().Be(2);
        }
    }

    [Fact]
    public async Task MasterUtilization_BookedDividedByWorkMinutes()
    {
        var day = DateTime.UtcNow.Date.AddDays(-1);
        using (var db = new AppDbContext(_options))
        {
            // Master A1: 8h schedule (480 minutes), 2 booked appointments of 60 min each = 120 booked.
            db.WorkSchedules.Add(new WorkSchedule
            {
                MasterId = _masterA1,
                BranchId = _branchA,
                WorkDate = DateOnly.FromDateTime(day),
                StartTime = new TimeOnly(10, 0),
                EndTime = new TimeOnly(18, 0),
                ScheduleType = ScheduleType.Work,
            });
            db.Bookings.AddRange(
                MakeBooking(_branchA, _masterA1, _serviceLong, _client1, day.AddHours(10), 60, 1000m, BookingStatus.Confirmed),
                MakeBooking(_branchA, _masterA1, _serviceLong, _client2, day.AddHours(12), 60, 1000m, BookingStatus.Completed),
                // Cancelled booking should NOT count toward utilization.
                MakeBooking(_branchA, _masterA1, _serviceLong, _client3, day.AddHours(14), 60, 1000m, BookingStatus.Cancelled));
            await db.SaveChangesAsync();
        }
        using (var db = new AppDbContext(_options))
        {
            var svc = new AnalyticsService(db);
            var snap = await svc.GetDashboardAsync(_branchA, DateOnly.FromDateTime(day), DateOnly.FromDateTime(day));
            var row = snap.Utilization.FirstOrDefault(u => u.MasterId == _masterA1);
            row.Should().NotBeNull();
            row!.BookedMinutes.Should().Be(120);
            row.WorkMinutes.Should().Be(480);
            row.UtilizationPercent.Should().Be(25.0m);
        }
    }

    [Fact]
    public async Task TopServices_OrderedByCount()
    {
        var day = DateTime.UtcNow.Date.AddDays(-1);
        using (var db = new AppDbContext(_options))
        {
            // Short × 3, Long × 1.
            db.Bookings.AddRange(
                MakeBooking(_branchA, _masterA1, _serviceShort, _client1, day.AddHours(10), 30, 500m, BookingStatus.Completed),
                MakeBooking(_branchA, _masterA1, _serviceShort, _client2, day.AddHours(11), 30, 500m, BookingStatus.Completed),
                MakeBooking(_branchA, _masterA1, _serviceShort, _client3, day.AddHours(12), 30, 500m, BookingStatus.Cancelled),
                MakeBooking(_branchA, _masterA1, _serviceLong, _client1, day.AddHours(13), 60, 1000m, BookingStatus.Completed));
            await db.SaveChangesAsync();
        }
        using (var db = new AppDbContext(_options))
        {
            var svc = new AnalyticsService(db);
            var snap = await svc.GetDashboardAsync(_branchA, DateOnly.FromDateTime(day), DateOnly.FromDateTime(day));
            snap.TopServices.Should().HaveCountGreaterOrEqualTo(2);
            snap.TopServices[0].ServiceId.Should().Be(_serviceShort);
            snap.TopServices[0].Count.Should().Be(3);
            snap.TopServices[0].Revenue.Should().Be(1000m); // only 2 completed × 500
            snap.TopServices[1].ServiceId.Should().Be(_serviceLong);
            snap.TopServices[1].Count.Should().Be(1);
            snap.TopServices[1].Revenue.Should().Be(1000m);
        }
    }

    [Fact]
    public async Task GetBranchComparison_OneRowPerActiveBranch()
    {
        var day = DateTime.UtcNow.Date.AddDays(-1);
        using (var db = new AppDbContext(_options))
        {
            db.Bookings.AddRange(
                MakeBooking(_branchA, _masterA1, _serviceShort, _client1, day.AddHours(10), 30, 500m, BookingStatus.Completed),
                MakeBooking(_branchB, _masterB1, _serviceShort, _client2, day.AddHours(11), 30, 500m, BookingStatus.Cancelled));
            await db.SaveChangesAsync();
        }
        using (var db = new AppDbContext(_options))
        {
            var svc = new AnalyticsService(db);
            var rows = await svc.GetBranchComparisonAsync(DateOnly.FromDateTime(day), DateOnly.FromDateTime(day));
            rows.Should().HaveCountGreaterOrEqualTo(2);
            var rowA = rows.First(r => r.BranchId == _branchA);
            var rowB = rows.First(r => r.BranchId == _branchB);
            rowA.TotalBookings.Should().Be(1);
            rowA.CompletedBookings.Should().Be(1);
            rowA.CancelRate.Should().Be(0m);
            rowB.TotalBookings.Should().Be(1);
            rowB.CompletedBookings.Should().Be(0);
            rowB.CancelRate.Should().Be(1m);
        }
    }

    [Fact]
    public async Task GetExportRows_ReturnsAllBookingsInRange()
    {
        var day = DateTime.UtcNow.Date.AddDays(-1);
        using (var db = new AppDbContext(_options))
        {
            db.Bookings.AddRange(
                MakeBooking(_branchA, _masterA1, _serviceShort, _client1, day.AddHours(10), 30, 500m, BookingStatus.Completed),
                MakeBooking(_branchB, _masterB1, _serviceShort, _client2, day.AddHours(11), 30, 500m, BookingStatus.Cancelled));
            await db.SaveChangesAsync();
        }
        using (var db = new AppDbContext(_options))
        {
            var svc = new AnalyticsService(db);
            var rows = await svc.GetExportRowsAsync(null, DateOnly.FromDateTime(day), DateOnly.FromDateTime(day));
            rows.Should().HaveCount(2);
            rows.Should().Contain(r => r.Status == BookingStatus.Completed);
            rows.Should().Contain(r => r.Status == BookingStatus.Cancelled);

            var csv = CsvExporter.BuildBookingsCsv(rows);
            csv.Length.Should().BeGreaterThan(3); // BOM + something
            // BOM (EF-BB-BF)
            csv[0].Should().Be(0xEF);
            csv[1].Should().Be(0xBB);
            csv[2].Should().Be(0xBF);

            var text = System.Text.Encoding.UTF8.GetString(csv, 3, csv.Length - 3);
            // Префикс sep= + русские заголовки + ';' разделитель для Excel ru-RU.
            text.Should().StartWith("sep=;\r\nID записи;Филиал;Мастер;Услуга;Клиент");
            // Статусы переведены через StatusLabels.
            text.Should().Contain("Завершена");
            text.Should().Contain("Отменена");
        }
    }

    [Fact]
    public void CsvExporter_HandlesQuotingAndSemicolons()
    {
        var rows = new List<BookingExportRow>
        {
            new(1, "Бранч с; точкой с запятой", "Иванов \"Иван\"", "Стрижка", "Клиент",
                new DateTime(2026, 1, 1, 10, 0, 0), 30, 500m, "Completed", "Online", null, null),
        };
        var csv = CsvExporter.BuildBookingsCsv(rows);
        var text = System.Text.Encoding.UTF8.GetString(csv, 3, csv.Length - 3);
        text.Should().Contain("\"Бранч с; точкой с запятой\"");
        text.Should().Contain("\"Иванов \"\"Иван\"\"\"");
    }

    private static Booking MakeBooking(int branchId, int masterId, int serviceId, int clientId,
        DateTime startUtc, int durationMin, decimal price, string status)
    {
        return new Booking
        {
            BranchId = branchId,
            MasterId = masterId,
            ServiceId = serviceId,
            ClientId = clientId,
            StartDateTime = startUtc,
            DurationMinutes = durationMin,
            PriceSnapshot = price,
            Status = status,
            Source = BookingSource.Online,
        };
    }
}
