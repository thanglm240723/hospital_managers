using Microsoft.AspNetCore.Mvc;

namespace CleanArchCqrs.API.Auth;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class InternalApiKeyAttribute : TypeFilterAttribute
{
    public InternalApiKeyAttribute() : base(typeof(InternalApiKeyFilter)) { }
}
