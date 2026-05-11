using BarbershopCrm.Infrastructure;
using BarbershopCrm.Infrastructure.Auth;
using BarbershopCrm.Infrastructure.Data;
using BarbershopCrm.Infrastructure.Security;
using BarbershopCrm.Domain.Enums;
using BarbershopCrm.Web.Auth;
using BarbershopCrm.Web.Services;
using BarbershopCrm.Infrastructure.Bookings;
using BarbershopCrm.Infrastructure.Scheduling;
using Microsoft.EntityFrameworkCore;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((ctx, services, lc) => lc
        .ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "BarbershopCrm"));

    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ICurrentUserAccessor, CurrentUserAccessor>();

    builder.Services.AddRazorPages(options =>
    {
        options.Conventions.ConfigureFilter(new Microsoft.AspNetCore.Mvc.TypeFilterAttribute(typeof(AuthorizePageFilter)));
    }).AddMvcOptions(o =>
    {
        var p = o.ModelBindingMessageProvider;
        p.SetValueIsInvalidAccessor(v => $"Значение «{v}» некорректно.");
        p.SetAttemptedValueIsInvalidAccessor((v, name) => $"Значение «{v}» в поле «{name}» некорректно.");
        p.SetUnknownValueIsInvalidAccessor(name => $"Значение в поле «{name}» некорректно.");
        p.SetMissingBindRequiredValueAccessor(name => $"Поле «{name}» обязательно.");
        p.SetMissingKeyOrValueAccessor(() => "Не указано значение.");
        p.SetMissingRequestBodyRequiredValueAccessor(() => "Пустое тело запроса.");
        p.SetValueMustNotBeNullAccessor(v => $"Значение «{v}» не может быть пустым.");
        p.SetNonPropertyAttemptedValueIsInvalidAccessor(v => $"Значение «{v}» некорректно.");
        p.SetNonPropertyUnknownValueIsInvalidAccessor(() => "Некорректное значение.");
        p.SetNonPropertyValueMustBeANumberAccessor(() => "Значение должно быть числом.");
        p.SetValueMustBeANumberAccessor(name => $"Поле «{name}» должно быть числом.");
    });

    builder.Services.AddAntiforgery();

    builder.Services.AddScoped<IImageUploadService, LocalImageUploadService>();
    builder.Services.AddScoped<ISlotService, SlotService>();

    builder.Services.Configure<BookingOptions>(builder.Configuration.GetSection("Booking"));
    builder.Services.AddScoped<IBookingService, BookingService>();

    builder.Services.AddNotificationBackground();

    var app = builder.Build();

    app.UseSerilogRequestLogging();

    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error");
        app.UseHsts();
    }

    app.UseHttpsRedirection();
    app.UseStaticFiles();
    app.UseRouting();

    app.UseMiddleware<SessionAuthMiddleware>();

    app.UseAuthorization();
    app.MapRazorPages();

    app.MapGet("/api/slots", async (
        int? branchId, int? serviceId, string? date, int? masterId,
        ISlotService slots, CancellationToken ct) =>
    {
        if (branchId is null || serviceId is null || string.IsNullOrWhiteSpace(date))
            return Results.BadRequest(new { error = "branchId, serviceId, date обязательны" });
        if (!DateOnly.TryParse(date, out var d))
            return Results.BadRequest(new { error = "Невалидная дата (ожидается YYYY-MM-DD)" });
        var items = await slots.GetFreeSlotsAsync(branchId.Value, serviceId.Value, d, masterId, ct);
        return Results.Ok(items);
    });

    if (app.Environment.IsDevelopment())
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();

        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("SeedDevData");
        await SeedDevData.ApplyAsync(db, hasher, logger);
    }

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program;
