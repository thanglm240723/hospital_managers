using CleanArchCqrs.Application.Common.Models;

namespace CleanArchCqrs.Application.Common.Interfaces;

public interface ITokenService
{
    /// JWT chỉ mang định danh ổn định: sub, fid (SessionFamily), sv (SecurityVersion). Không role/permission.
    AccessToken CreateAccessToken(Guid userId, Guid sessionFamilyId, int securityVersion);
}
