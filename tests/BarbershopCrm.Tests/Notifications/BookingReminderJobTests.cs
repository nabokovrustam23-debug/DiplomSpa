using BarbershopCrm.Domain.Enums;
using BarbershopCrm.Infrastructure.Data;
using BarbershopCrm.Infrastructure.Notifications;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BarbershopCrm.Tests.Notifications;

public class BookingReminderJobTests : IAsyncLifetime
{
    private NotificationsTestFixture _fx = null!;

    public async Task InitializeAsync() => _fx = await NotificationsTestFixture.CreateAsync();
    public async Task DisposeAsync() => await _fx.DisposeAsync();

    private BookingReminderJob BuildJob(NotificationOptions? opts = null)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(_fx.Connection));
        var sp = services.BuildServiceProvider();
        return new BookingReminderJob(sp,
            Options.Create(opts ?? new NotificationOptions { BackgroundEnabled = false, ReminderIntervalMinutes = 60, ReminderHoursBefore = new[] { 24, 2 } }),
            NullLogger<BookingReminderJob>.Instance);
    }

    [Fact]
    public async Task ProcessOneTick_creates_reminders_for_bookings_24h_ahead()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        // Booking ~24h away — within window.
        var bid = await _fx.CreateBookingAsync(now.AddHours(24).AddMinutes(15));
        var job = BuildJob();
        var created = await job.ProcessOneTickAsync(now, CancellationToken.None);
        created.Should().Be(2); // email + in-app

        await using var read = _fx.NewContext();
        var rows = await read.Notifications.Where(n => n.RelatedBookingId == bid).ToListAsync();
        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(n => n.Subject!.Contains(BookingReminderJob.SubjectPrefix(24)));
    }

    [Fact]
    public async Task ProcessOneTick_creates_reminders_for_bookings_2h_ahead()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var bid = await _fx.CreateBookingAsync(now.AddHours(2).AddMinutes(15));
        var job = BuildJob();
        var created = await job.ProcessOneTickAsync(now, CancellationToken.None);
        created.Should().Be(2);

        await using var read = _fx.NewContext();
        var rows = await read.Notifications.Where(n => n.RelatedBookingId == bid).ToListAsync();
        rows.Should().OnlyContain(n => n.Subject!.Contains(BookingReminderJob.SubjectPrefix(2)));
    }

    [Fact]
    public async Task ProcessOneTick_skips_bookings_outside_window()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        // 3 hours away — neither 24h nor 2h window.
        var bid = await _fx.CreateBookingAsync(now.AddHours(3));
        var job = BuildJob();
        var created = await job.ProcessOneTickAsync(now, CancellationToken.None);
        created.Should().Be(0);
        (await _fx.NewContext().Notifications.AnyAsync(n => n.RelatedBookingId == bid)).Should().BeFalse();
    }

    [Fact]
    public async Task ProcessOneTick_is_idempotent_on_repeat_runs()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var bid = await _fx.CreateBookingAsync(now.AddHours(24).AddMinutes(15));
        var job = BuildJob();

        var c1 = await job.ProcessOneTickAsync(now, CancellationToken.None);
        var c2 = await job.ProcessOneTickAsync(now, CancellationToken.None);
        c1.Should().Be(2);
        c2.Should().Be(0); // already created, no duplicates

        var rows = await _fx.NewContext().Notifications.CountAsync(n => n.RelatedBookingId == bid);
        rows.Should().Be(2);
    }

    [Fact]
    public async Task ProcessOneTick_skips_cancelled_and_completed_bookings()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        await _fx.CreateBookingAsync(now.AddHours(24).AddMinutes(10), status: BookingStatus.Cancelled);
        await _fx.CreateBookingAsync(now.AddHours(24).AddMinutes(20), status: BookingStatus.Completed);
        await _fx.CreateBookingAsync(now.AddHours(24).AddMinutes(30), status: BookingStatus.NoShow);
        var job = BuildJob();
        var created = await job.ProcessOneTickAsync(now, CancellationToken.None);
        created.Should().Be(0);
    }

    [Fact]
    public async Task ProcessOneTick_creates_only_inapp_when_client_has_no_email()
    {
        var noEmailClient = await _fx.NewContext().Clients
            .FirstAsync(c => c.PersonaId == _fx.ClientNoEmailPersonaId);
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var bid = await _fx.CreateBookingAsync(now.AddHours(24).AddMinutes(20), clientId: noEmailClient.ClientId);
        var job = BuildJob();
        var created = await job.ProcessOneTickAsync(now, CancellationToken.None);
        created.Should().Be(1);

        var rows = await _fx.NewContext().Notifications
            .Where(n => n.RelatedBookingId == bid).ToListAsync();
        rows.Should().HaveCount(1);
        rows[0].Channel.Should().Be(NotificationChannel.InApp);
    }

    [Fact]
    public async Task ProcessOneTick_handles_both_24h_and_2h_in_one_call()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var b24 = await _fx.CreateBookingAsync(now.AddHours(24).AddMinutes(10));
        var b2 = await _fx.CreateBookingAsync(now.AddHours(2).AddMinutes(10));
        var job = BuildJob();
        var created = await job.ProcessOneTickAsync(now, CancellationToken.None);
        created.Should().Be(4); // 2 reminders × 2 bookings (each = email+inapp)

        var rows = await _fx.NewContext().Notifications.Where(n => n.RelatedBookingId == b24 || n.RelatedBookingId == b2).ToListAsync();
        rows.Where(n => n.RelatedBookingId == b24).Should().AllSatisfy(n => n.Subject!.StartsWith(BookingReminderJob.SubjectPrefix(24)).Should().BeTrue());
        rows.Where(n => n.RelatedBookingId == b2).Should().AllSatisfy(n => n.Subject!.StartsWith(BookingReminderJob.SubjectPrefix(2)).Should().BeTrue());
    }

    [Fact]
    public void SubjectPrefix_is_stable_and_distinct_per_window()
    {
        BookingReminderJob.SubjectPrefix(24).Should().Be("[Reminder-24h]");
        BookingReminderJob.SubjectPrefix(2).Should().Be("[Reminder-2h]");
        BookingReminderJob.SubjectPrefix(24).Should().NotBe(BookingReminderJob.SubjectPrefix(2));
    }
}
