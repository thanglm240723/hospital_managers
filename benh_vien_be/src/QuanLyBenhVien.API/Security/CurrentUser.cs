using Microsoft.AspNetCore.Http;
using QuanLyBenhVien.Application.Common.Identity;

namespace QuanLyBenhVien.API.Security;

public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public Guid? UserId => GuidClaim("sub");

    public Guid? SessionFamilyId => GuidClaim("fid");

    public int? SecurityVersion
        => int.TryParse(httpContextAccessor.HttpContext?.User?.FindFirst("sv")?.Value, out var sv) ? sv : null;

    private Guid? GuidClaim(string type)
        => Guid.TryParse(httpContextAccessor.HttpContext?.User?.FindFirst(type)?.Value, out var id) ? id : null;
}
