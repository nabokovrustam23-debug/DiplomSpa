using BarbershopCrm.Infrastructure.Auth;
using Microsoft.AspNetCore.Http;

namespace BarbershopCrm.Web.Auth;

internal static class SessionCookie
{
    public static void Set(HttpContext ctx, AuthOptions opts, string token, DateTime expiresAtUtc)
    {
        ctx.Response.Cookies.Append(opts.SessionCookieName, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = ctx.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Expires = expiresAtUtc,
            Path = "/",
            IsEssential = true,
        });
    }

    public static void Clear(HttpContext ctx, AuthOptions opts)
    {
        ctx.Response.Cookies.Delete(opts.SessionCookieName, new CookieOptions
        {
            Path = "/",
        });
    }
}
