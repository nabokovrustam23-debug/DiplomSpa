using BarbershopCrm.Infrastructure;
using BarbershopCrm.Infrastructure.Auth;
using BarbershopCrm.Infrastructure.Data;
using BarbershopCrm.Infrastructure.Security;
using BarbershopCrm.Domain.Enums;
using BarbershopCrm.Web.Auth;
using BarbershopCrm.Web.Services;
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
    });

    builder.Services.AddAntiforgery();

    builder.Services.Configure<DaDataOptions>(builder.Configuration.GetSection(DaDataOptions.SectionName));
    builder.Services.PostConfigure<DaDataOptions>(opt =>
    {
        // Allow env var override (DADATA_API_KEY) for prod / dev secrets.
        var fromEnv = Environment.GetEnvironmentVariable("DADATA_API_KEY");
        if (!string.IsNullOrWhiteSpace(fromEnv))
            opt.ApiKey = fromEnv;
    });
    builder.Services.AddHttpClient<IAddressSuggestService, DaDataAddressSuggestService>(c =>
    {
        c.Timeout = TimeSpan.FromSeconds(5);
    });

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

    app.MapGet("/api/address/suggest", async (
        string? q,
        IAddressSuggestService svc,
        ICurrentUserAccessor currentUser,
        CancellationToken ct) =>
    {
        if (!currentUser.IsAuthenticated)
            return Results.StatusCode(StatusCodes.Status401Unauthorized);

        if (!currentUser.IsInRole(RoleCode.Owner))
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 3)
            return Results.Ok(Array.Empty<AddressSuggestion>());

        var items = await svc.SuggestAsync(q.Trim(), ct);
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
