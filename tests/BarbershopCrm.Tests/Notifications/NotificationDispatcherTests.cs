using BarbershopCrm.Domain.Entities;
using BarbershopCrm.Domain.Enums;
using BarbershopCrm.Infrastructure.Data;
using BarbershopCrm.Infrastructure.Email;
using BarbershopCrm.Infrastructure.Notifications;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BarbershopCrm.Tests.Notifications;

public class NotificationDispatcherTests : IAsyncLifetime
{
    private NotificationsTestFixture _fx = null!;

    public async Task InitializeAsync() => _fx = await NotificationsTestFixture.CreateAsync();
    public async Task DisposeAsync() => await _fx.DisposeAsync();

    private sealed class CapturingEmailSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = new();
        public bool ShouldThrow { get; set; }
        public Task SendAsync(EmailMessage m, CancellationToken ct = default)
        {
            if (ShouldThrow) throw new InvalidOperationException("smtp boom");
            Sent.Add(m);
            return Task.CompletedTask;
        }
    }

    private (NotificationDispatcher dispatcher, CapturingEmailSender email) BuildDispatcher(
        NotificationOptions? opts = null, bool throwOnSend = false)
    {
        var email = new CapturingEmailSender { ShouldThrow = throwOnSend };
        var services = new ServiceCollection();
        services.AddSingleton<IEmailSender>(email);
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(_fx.Connection));
        var sp = services.BuildServiceProvider();
        var d = new NotificationDispatcher(sp,
            Options.Create(opts ?? new NotificationOptions { BackgroundEnabled = false }),
            NullLogger<NotificationDispatcher>.Instance);
        return (d, email);
    }

    [Fact]
    public async Task ProcessOneTick_sends_email_and_marks_sent()
    {
        await using (var db = _fx.NewContext())
        {
            db.Notifications.Add(new Notification
            {
                RecipientPersonaId = _fx.ClientPersonaId,
                Channel = NotificationChannel.Email,
                Subject = "Test", Body = "Body",
                Status = NotificationStatus.Pending,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var (d, email) = BuildDispatcher();
        await d.ProcessOneTickAsync(CancellationToken.None);

        email.Sent.Should().ContainSingle();
        email.Sent[0].To.Should().Be("client@thq.test");
        email.Sent[0].Subject.Should().Be("Test");

        await using var read = _fx.NewContext();
        var n = await read.Notifications.FirstAsync();
        n.Status.Should().Be(NotificationStatus.Sent);
        n.SentAt.Should().NotBeNull();
        n.Error.Should().BeNull();
    }

    [Fact]
    public async Task ProcessOneTick_inapp_marks_sent_without_calling_email()
    {
        await using (var db = _fx.NewContext())
        {
            db.Notifications.Add(new Notification
            {
                RecipientPersonaId = _fx.ClientPersonaId,
                Channel = NotificationChannel.InApp,
                Subject = "I", Body = "B",
                Status = NotificationStatus.Pending,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var (d, email) = BuildDispatcher();
        await d.ProcessOneTickAsync(CancellationToken.None);

        email.Sent.Should().BeEmpty();
        await using var read = _fx.NewContext();
        var n = await read.Notifications.FirstAsync();
        n.Status.Should().Be(NotificationStatus.Sent);
        n.SentAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ProcessOneTick_marks_failed_when_email_sender_throws()
    {
        await using (var db = _fx.NewContext())
        {
            db.Notifications.Add(new Notification
            {
                RecipientPersonaId = _fx.ClientPersonaId,
                Channel = NotificationChannel.Email,
                Subject = "X", Body = "Y",
                Status = NotificationStatus.Pending,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var (d, _) = BuildDispatcher(throwOnSend: true);
        await d.ProcessOneTickAsync(CancellationToken.None);

        await using var read = _fx.NewContext();
        var n = await read.Notifications.FirstAsync();
        n.Status.Should().Be(NotificationStatus.Failed);
        n.Error.Should().Contain("smtp boom");
        n.SentAt.Should().BeNull();
    }

    [Fact]
    public async Task ProcessOneTick_marks_failed_when_recipient_has_no_email()
    {
        await using (var db = _fx.NewContext())
        {
            db.Notifications.Add(new Notification
            {
                RecipientPersonaId = _fx.ClientNoEmailPersonaId,
                Channel = NotificationChannel.Email,
                Subject = "X", Body = "Y",
                Status = NotificationStatus.Pending,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var (d, email) = BuildDispatcher();
        await d.ProcessOneTickAsync(CancellationToken.None);

        email.Sent.Should().BeEmpty();
        await using var read = _fx.NewContext();
        var n = await read.Notifications.FirstAsync();
        n.Status.Should().Be(NotificationStatus.Failed);
        n.Error.Should().Contain("no email");
    }

    [Fact]
    public async Task ProcessOneTick_sms_marks_failed_with_stub_error()
    {
        await using (var db = _fx.NewContext())
        {
            db.Notifications.Add(new Notification
            {
                RecipientPersonaId = _fx.ClientPersonaId,
                Channel = NotificationChannel.Sms,
                Subject = "S", Body = "B",
                Status = NotificationStatus.Pending,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var (d, _) = BuildDispatcher();
        await d.ProcessOneTickAsync(CancellationToken.None);

        await using var read = _fx.NewContext();
        var n = await read.Notifications.FirstAsync();
        n.Status.Should().Be(NotificationStatus.Failed);
        n.Error.Should().Contain("SMS gateway");
    }

    [Fact]
    public async Task ProcessOneTick_processes_in_creation_order_and_respects_batch_size()
    {
        await using (var db = _fx.NewContext())
        {
            for (int i = 0; i < 5; i++)
            {
                db.Notifications.Add(new Notification
                {
                    RecipientPersonaId = _fx.ClientPersonaId,
                    Channel = NotificationChannel.InApp,
                    Subject = $"S{i}", Body = "x",
                    Status = NotificationStatus.Pending,
                    CreatedAt = DateTime.UtcNow.AddMinutes(i),
                });
            }
            await db.SaveChangesAsync();
        }

        var (d, _) = BuildDispatcher(new NotificationOptions { BatchSize = 3, BackgroundEnabled = false });
        await d.ProcessOneTickAsync(CancellationToken.None);

        await using var read = _fx.NewContext();
        var processed = await read.Notifications.Where(n => n.Status == NotificationStatus.Sent)
            .OrderBy(n => n.CreatedAt).Select(n => n.Subject).ToListAsync();
        processed.Should().Equal(new[] { "S0", "S1", "S2" });

        // Run again — picks up the rest.
        await d.ProcessOneTickAsync(CancellationToken.None);
        var allSent = await _fx.NewContext().Notifications.CountAsync(n => n.Status == NotificationStatus.Sent);
        allSent.Should().Be(5);
    }

    [Fact]
    public async Task ProcessOneTick_isolates_failure_per_row()
    {
        await using (var db = _fx.NewContext())
        {
            // First row will fail (no email recipient), second will succeed (in-app).
            db.Notifications.AddRange(
                new Notification { RecipientPersonaId = _fx.ClientNoEmailPersonaId, Channel = NotificationChannel.Email, Subject = "F", Body = "x", Status = NotificationStatus.Pending, CreatedAt = DateTime.UtcNow.AddMinutes(-5) },
                new Notification { RecipientPersonaId = _fx.ClientPersonaId, Channel = NotificationChannel.InApp, Subject = "S", Body = "x", Status = NotificationStatus.Pending, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var (d, _) = BuildDispatcher();
        await d.ProcessOneTickAsync(CancellationToken.None);

        await using var read = _fx.NewContext();
        (await read.Notifications.FirstAsync(n => n.Subject == "F")).Status.Should().Be(NotificationStatus.Failed);
        (await read.Notifications.FirstAsync(n => n.Subject == "S")).Status.Should().Be(NotificationStatus.Sent);
    }
}
