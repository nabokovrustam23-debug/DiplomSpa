using BarbershopCrm.Infrastructure.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;

namespace BarbershopCrm.Web.Auth;

/// <summary>
/// Global Razor Page filter: if a page (or any class in its inheritance chain) is decorated
/// with <see cref="AuthorizePageAttribute"/>, the request must come from an authenticated
/// session, and the user's role must match the attribute's allow-list (if specified).
/// </summary>
public sealed class AuthorizePageFilter : IAsyncPageFilter
{
    private readonly ICurrentUserAccessor _currentUser;

    public AuthorizePageFilter(ICurrentUserAccessor currentUser)
    {
        _currentUser = currentUser;
    }

    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context)
        => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var attr = ResolveAttribute(context.HandlerInstance, context.ActionDescriptor);
        if (attr is null)
        {
            await next();
            return;
        }

        if (!_currentUser.IsAuthenticated)
        {
            var returnUrl = context.HttpContext.Request.Path + context.HttpContext.Request.QueryString;
            context.Result = new RedirectToPageResult("/Account/Login", new { returnUrl = returnUrl.ToString() });
            return;
        }

        if (attr.Roles.Length > 0 && !attr.Roles.Contains(_currentUser.User!.RoleCode))
        {
            context.Result = new StatusCodeResult(403);
            return;
        }

        await next();
    }

    private static AuthorizePageAttribute? ResolveAttribute(object? handlerInstance, CompiledPageActionDescriptor? descriptor)
    {
        // 1. From the handler instance type (works for nested page models / inheritance chains).
        if (handlerInstance is PageModel pm)
        {
            var fromType = pm.GetType().GetCustomAttributes(typeof(AuthorizePageAttribute), inherit: true)
                .OfType<AuthorizePageAttribute>().FirstOrDefault();
            if (fromType is not null)
                return fromType;
        }

        // 2. From the compiled descriptor (covers cases where handlerInstance is null).
        if (descriptor is not null)
        {
            return descriptor.HandlerTypeInfo
                .GetCustomAttributes(typeof(AuthorizePageAttribute), inherit: true)
                .OfType<AuthorizePageAttribute>()
                .FirstOrDefault();
        }

        return null;
    }
}
