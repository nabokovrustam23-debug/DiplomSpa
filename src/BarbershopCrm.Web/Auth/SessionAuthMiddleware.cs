using BarbershopCrm.Infrastructure.Auth;
using Microsoft.Extensions.Options;

namespace BarbershopCrm.Web.Auth;

/// <summary>
/// Reads the session cookie, resolves the user via <see cref="IUserAuthService"/> and
/// stashes it in <see cref="HttpContext.Items"/> for the rest of the pipeline.
/// </summary>
public sealed class SessionAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly AuthOptions _options;

    public SessionAuthMiddleware(RequestDelegate next, IOptions<AuthOptions> options)
    {
        _next = next;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext ctx, IUserAuthService auth)
    {
        var token = ctx.Request.Cookies[_options.SessionCookieName];
        if (!string.IsNullOrEmpty(token))
        {
            var current = await auth.ResolveSessionAsync(token, ctx.RequestAborted);
            if (current is not null)
            {
                ctx.Items[CurrentUserAccessor.HttpContextItemKey] = current;
            }
            else
            {
                // Stale cookie — drop it so the browser stops sending it.
                ctx.Response.Cookies.Delete(_options.SessionCookieName);
            }
        }

        await _next(ctx);
    }
}
