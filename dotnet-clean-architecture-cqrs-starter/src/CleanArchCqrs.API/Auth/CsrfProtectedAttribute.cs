using Microsoft.AspNetCore.Mvc;

namespace CleanArchCqrs.API.Auth;

[AttributeUsage(AttributeTargets.Method)]
public sealed class CsrfProtectedAttribute : TypeFilterAttribute
{
    public CsrfProtectedAttribute() : base(typeof(CsrfProtectionFilter)) { }
}
