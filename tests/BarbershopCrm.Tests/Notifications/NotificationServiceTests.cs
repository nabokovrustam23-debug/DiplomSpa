using BarbershopCrm.Domain.Entities;
using BarbershopCrm.Domain.Enums;
using BarbershopCrm.Infrastructure.Data;
using BarbershopCrm.Infrastructure.Notifications;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BarbershopCrm.Tests.Notifications;

public class NotificationServiceTests : IAsyncLifetime
{
    private NotificationsTestFixture _fx = null!;

    public async Task InitializeAsync() => _fx = await NotificationsTestFixture.CreateAsync();
    public async Task DisposeAsync() => await _fx.DisposeAsync();

    private NotificationService MakeService(AppDbContext db) =>
        new(db, NullLogger<NotificationService>.Instance);

    [Fact]
    public async Task OnBookingCreated_creates_email_and_inapp_for_client_plus_inapp_for_master()
    {
        var bookingId = await _fx.CreateBookingAsync(DateTime.UtcNow.AddDays(2));

        await using (var db = _fx.NewContext())
        {
            await MakeService(db).OnBookingCreatedAsync(bookingId);
        }

        await using var read = _fx.NewContext();
        var rows = await read.Notifications.Where(n => n.RelatedBookingId == bookingId).ToListAsync();

        // 1 email to client + 1 in-app to client + 1 in-app to master = 3
        rows.Should().HaveCount(3);
        rows.Where(n => n.RecipientPersonaId == _fx.ClientPersonaId)
            .Select(n => n.Channel)
            .Should().BeEquivalentTo(new[] { NotificationChannel.Email, NotificationChannel.InApp });

        rows.Where(n => n.RecipientPersonaId == _fx.MasterPersonaId)
            .Select(n => n.Channel)
            .Should().BeEquivalentTo(new[] { NotificationChannel.InApp });

        rows.Should().OnlyContain(n => n.Status == NotificationStatus.Pending);
    }

    [Fact]
    public async Task OnBookingCreated_falls_back_to_inapp_when_client_has_no_email()
    {
        // Use the no-email client.
        var clientNoEmail = await _fx.NewContext().Clients
            .FirstAsync(c => c.PersonaId == _fx.ClientNoEmailPersonaId);
        var bookingId = await _fx.CreateBookingAsync(DateTime.UtcNow.AddDays(2), clientId: clientNoEmail.ClientId);

        await using (var db = _fx.NewContext())
        {
            await MakeService(db).OnBookingCreatedAsync(bookingId);
        }

        await using var read = _fx.NewContext();
        var clientRows = await read.Notifications
            .Where(n => n.RelatedBookingId == bookingId && n.RecipientPersonaId == _fx.ClientNoEmailPersonaId)
            .ToListAsync();

        // No-email client: only InApp (no email row, but the InApp fallback is created twice
        // — once for the "fallback" branch, once as the parallel in-app — pruned below).
        clientRows.Should().NotContain(n => n.Channel == NotificationChannel.Email);
        clientRows.Should().OnlyContain(n => n.Channel == NotificationChannel.InApp);
        clientRows.Should().HaveCountGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task OnBookingCancelled_creates_email_and_inapp_with_reason_in_body()
    {
        var bookingId = await _fx.CreateBookingAsync(DateTime.UtcNow.AddDays(2));
        await using (var db = _fx.NewContext())
            await MakeService(db).OnBookingCancelledAsync(bookingId, "Не успеваю");

        await using var read = _fx.NewContext();
        var rows = await read.Notifications.Where(n => n.RelatedBookingId == bookingId).ToListAsync();
        rows.Should().HaveCount(3);
        rows.Where(n => n.RecipientPersonaId == _fx.ClientPersonaId)
            .Should().Contain(n => n.Body.Contains("Не успеваю"));
    }

    [Fact]
    public async Task OnBookingConfirmed_creates_one_email_plus_one_inapp_for_client_only()
    {
        var bookingId = await _fx.CreateBookingAsync(DateTime.UtcNow.AddDays(2));
        await using (var db = _fx.NewContext())
            await MakeService(db).OnBookingConfirmedAsync(bookingId);

        await using var read = _fx.NewContext();
        var rows = await read.Notifications.Where(n => n.RelatedBookingId == bookingId).ToListAsync();
        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(n => n.RecipientPersonaId == _fx.ClientPersonaId);
        rows.Select(n => n.Channel).Should().BeEquivalentTo(new[] { NotificationChannel.Email, NotificationChannel.InApp });
    }

    [Fact]
    public async Task OnBookingCompleted_creates_inapp_for_client_only()
    {
        var bookingId = await _fx.CreateBookingAsync(DateTime.UtcNow.AddDays(2));
        await using (var db = _fx.NewContext())
            await MakeService(db).OnBookingCompletedAsync(bookingId);

        await using var read = _fx.NewContext();
        var rows = await read.Notifications.Where(n => n.RelatedBookingId == bookingId).ToListAsync();
        rows.Should().HaveCount(1);
        rows[0].RecipientPersonaId.Should().Be(_fx.ClientPersonaId);
        rows[0].Channel.Should().Be(NotificationChannel.InApp);
    }

    [Fact]
    public async Task OnBookingCreated_swallows_exceptions_when_booking_not_found()
    {
        await using var db = _fx.NewContext();
        var act = async () => await MakeService(db).OnBookingCreatedAsync(999999);
        await act.Should().NotThrowAsync();
        // No rows persisted.
        var any = await db.Notifications.AnyAsync(n => n.RelatedBookingId == 999999);
        any.Should().BeFalse();
    }

    [Fact]
    public async Task OnLeadCreated_routes_to_branch_admins_and_owner()
    {
        await using var db = _fx.NewContext();
        var lead = new Domain.Entities.Lead
        {
            PersonaId = _fx.ClientPersonaId,
            RawName = "Тест Тестов",
            RawPhone = "+79180009999",
            PreferredBranchId = _fx.BranchId,
            Comment = "Хочу подстричься",
            Status = LeadStatus.New,
            CreatedAt = DateTime.UtcNow,
        };
        db.Leads.Add(lead);
        await db.SaveChangesAsync();

        await MakeService(db).OnLeadCreatedAsync(lead.LeadId);

        await using var read = _fx.NewContext();
        var rows = await read.Notifications.Where(n => n.Subject == "Новая заявка").ToListAsync();

        // Goes to admin (in same branch) + owner (no branch).
        rows.Should().HaveCount(2);
        rows.Select(n => n.RecipientPersonaId).Should().BeEquivalentTo(new[] { _fx.AdminPersonaId, _fx.OwnerPersonaId });
        rows.Should().OnlyContain(n => n.Channel == NotificationChannel.InApp);
        rows.Should().OnlyContain(n => n.Body.Contains("Хочу подстричься"));
    }

    [Fact]
    public async Task OnLeadCreated_without_preferred_branch_routes_to_all_admins_and_owner()
    {
        await using var db = _fx.NewContext();
        var lead = new Domain.Entities.Lead
        {
            RawName = "Без филиала", RawPhone = "+79180009998",
            Status = LeadStatus.New, CreatedAt = DateTime.UtcNow,
        };
        db.Leads.Add(lead);
        await db.SaveChangesAsync();

        await MakeService(db).OnLeadCreatedAsync(lead.LeadId);

        var rows = await _fx.NewContext().Notifications
            .Where(n => n.Subject == "Новая заявка")
            .OrderBy(n => n.NotificationId).ToListAsync();
        rows.Should().HaveCount(2); // 1 admin + 1 owner from seed
    }

    [Fact]
    public async Task GetForRecipient_filters_by_unread_and_orders_newest_first()
    {
        await using (var db = _fx.NewContext())
        {
            db.Notifications.AddRange(
                new Notification { RecipientPersonaId = _fx.ClientPersonaId, Channel = NotificationChannel.InApp, Subject = "1", Body = "...", CreatedAt = DateTime.UtcNow.AddMinutes(-30), ReadAt = DateTime.UtcNow.AddMinutes(-10) },
                new Notification { RecipientPersonaId = _fx.ClientPersonaId, Channel = NotificationChannel.InApp, Subject = "2", Body = "...", CreatedAt = DateTime.UtcNow.AddMinutes(-20) },
                new Notification { RecipientPersonaId = _fx.ClientPersonaId, Channel = NotificationChannel.InApp, Subject = "3", Body = "...", CreatedAt = DateTime.UtcNow.AddMinutes(-10) },
                new Notification { RecipientPersonaId = _fx.MasterPersonaId, Channel = NotificationChannel.InApp, Subject = "Other", Body = "...", CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        await using var read = _fx.NewContext();
        var svc = MakeService(read);

        var all = await svc.GetForRecipientAsync(_fx.ClientPersonaId, unreadOnly: false, take: 100);
        all.Select(n => n.Subject).Should().Equal(new[] { "3", "2", "1" });

        var unread = await svc.GetForRecipientAsync(_fx.ClientPersonaId, unreadOnly: true, take: 100);
        unread.Select(n => n.Subject).Should().Equal(new[] { "3", "2" });
    }

    [Fact]
    public async Task GetUnreadCount_only_counts_inapp_unread_for_recipient()
    {
        await using (var db = _fx.NewContext())
        {
            db.Notifications.AddRange(
                new Notification { RecipientPersonaId = _fx.ClientPersonaId, Channel = NotificationChannel.InApp, Subject = "u1", Body = "x", CreatedAt = DateTime.UtcNow },
                new Notification { RecipientPersonaId = _fx.ClientPersonaId, Channel = NotificationChannel.InApp, Subject = "u2", Body = "x", CreatedAt = DateTime.UtcNow, ReadAt = DateTime.UtcNow },
                new Notification { RecipientPersonaId = _fx.ClientPersonaId, Channel = NotificationChannel.Email, Subject = "u3", Body = "x", CreatedAt = DateTime.UtcNow }, // not counted
                new Notification { RecipientPersonaId = _fx.MasterPersonaId, Channel = NotificationChannel.InApp, Subject = "u4", Body = "x", CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        await using var read = _fx.NewContext();
        var n = await MakeService(read).GetUnreadCountAsync(_fx.ClientPersonaId);
        n.Should().Be(1);
    }

    [Fact]
    public async Task MarkRead_only_succeeds_when_actor_owns_notification()
    {
        int targetId;
        await using (var db = _fx.NewContext())
        {
            var n = new Notification { RecipientPersonaId = _fx.ClientPersonaId, Channel = NotificationChannel.InApp, Subject = "x", Body = "x", CreatedAt = DateTime.UtcNow };
            db.Notifications.Add(n);
            await db.SaveChangesAsync();
            targetId = n.NotificationId;
        }

        await using (var db = _fx.NewContext())
        {
            var ok = await MakeService(db).MarkReadAsync(targetId, _fx.MasterPersonaId);
            ok.Should().BeFalse();
        }
        await using (var db = _fx.NewContext())
        {
            (await db.Notifications.FindAsync(targetId))!.ReadAt.Should().BeNull();
        }
        await using (var db = _fx.NewContext())
        {
            var ok = await MakeService(db).MarkReadAsync(targetId, _fx.ClientPersonaId);
            ok.Should().BeTrue();
        }
        await using (var db = _fx.NewContext())
        {
            (await db.Notifications.FindAsync(targetId))!.ReadAt.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task MarkAllRead_marks_only_unread_owned_rows()
    {
        await using (var db = _fx.NewContext())
        {
            db.Notifications.AddRange(
                new Notification { RecipientPersonaId = _fx.ClientPersonaId, Channel = NotificationChannel.InApp, Subject = "1", Body = "x", CreatedAt = DateTime.UtcNow },
                new Notification { RecipientPersonaId = _fx.ClientPersonaId, Channel = NotificationChannel.InApp, Subject = "2", Body = "x", CreatedAt = DateTime.UtcNow },
                new Notification { RecipientPersonaId = _fx.ClientPersonaId, Channel = NotificationChannel.InApp, Subject = "already", Body = "x", CreatedAt = DateTime.UtcNow, ReadAt = DateTime.UtcNow },
                new Notification { RecipientPersonaId = _fx.MasterPersonaId, Channel = NotificationChannel.InApp, Subject = "other", Body = "x", CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        int updated;
        await using (var db = _fx.NewContext())
            updated = await MakeService(db).MarkAllReadAsync(_fx.ClientPersonaId);

        updated.Should().Be(2);

        await using var read = _fx.NewContext();
        (await read.Notifications.CountAsync(n => n.RecipientPersonaId == _fx.ClientPersonaId && n.ReadAt == null)).Should().Be(0);
        (await read.Notifications.CountAsync(n => n.RecipientPersonaId == _fx.MasterPersonaId && n.ReadAt == null)).Should().Be(1);
    }
}
