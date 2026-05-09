namespace BarbershopCrm.Web.Auth;

/// <summary>
/// Marks a Razor Page (or its base class) as requiring an authenticated session.
/// Optionally restricts access to specific role codes.
/// Enforced by <see cref="AuthorizePageFilter"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class AuthorizePageAttribute : Attribute
{
    public string[] Roles { get; }

    public AuthorizePageAttribute(params string[] roles)
    {
        Roles = roles ?? Array.Empty<string>();
    }
}
