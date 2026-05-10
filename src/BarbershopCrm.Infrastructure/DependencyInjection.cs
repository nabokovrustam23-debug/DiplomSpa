using BarbershopCrm.Infrastructure.Analytics;
using BarbershopCrm.Infrastructure.Auth;
using BarbershopCrm.Infrastructure.Data;
using BarbershopCrm.Infrastructure.Email;
using BarbershopCrm.Infrastructure.Notifications;
using BarbershopCrm.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BarbershopCrm.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "Connection string 'Default' is required (appsettings.json).");

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite(connectionString, sqlite =>
                sqlite.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)));

        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<ITokenGenerator, TokenGenerator>();
        services.AddSingleton(TimeProvider.System);

        services.AddOptions<AuthOptions>()
            .Bind(configuration.GetSection(AuthOptions.SectionName))
            .ValidateOnStart();

        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName));

        services.AddOptions<NotificationOptions>()
            .Bind(configuration.GetSection(NotificationOptions.SectionName));

        services.AddScoped<IUserAuthService, UserAuthService>();
        services.AddScoped<IAnalyticsService, AnalyticsService>();
        services.AddScoped<INotificationService, NotificationService>();

        // Email provider — selectable via config. Default is "Log" (writes to Serilog).
        var provider = configuration.GetSection(EmailOptions.SectionName)["Provider"] ?? "Log";
        if (string.Equals(provider, "Smtp", StringComparison.OrdinalIgnoreCase))
        {
            services.AddScoped<IEmailSender, SmtpEmailSender>();
        }
        else
        {
            services.AddSingleton<IEmailSender, LogEmailSender>();
        }

        return services;
    }

    /// <summary>
    /// Registers the notification dispatcher and reminder job hosted services.
    /// Tests skip this method to keep background work out of the test harness.
    /// </summary>
    public static IServiceCollection AddNotificationBackground(this IServiceCollection services)
    {
        services.AddHostedService<NotificationDispatcher>();
        services.AddHostedService<BookingReminderJob>();
        return services;
    }
}
