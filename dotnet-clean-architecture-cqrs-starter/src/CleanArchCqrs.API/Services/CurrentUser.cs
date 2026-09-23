using CleanArchCqrs.Application.Common.Interfaces;

namespace CleanArchCqrs.API.Services;

public sealed class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUser(IHttpContextAccessor httpContextAccessor) => _httpContextAccessor = httpContextAccessor;

    public Guid? UserId => GuidClaim("sub");

    public Guid? SessionFamilyId => GuidClaim("fid");

    public bool IsAuthenticated => _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;

    private Guid? GuidClaim(string type)
        => Guid.TryParse(_httpContextAccessor.HttpContext?.User?.FindFirst(type)?.Value, out var id) ? id : null;
}
