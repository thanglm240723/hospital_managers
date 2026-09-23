using CleanArchCqrs.Application.Common.Models;

namespace CleanArchCqrs.Application.Common.Interfaces;

/// Quyền HÀNH ĐỘNG của user. Quyền theo tài nguyên (phân công, grant khẩn cấp) KHÔNG nằm ở đây — kiểm trong DB theo use case.
public interface IPermissionService
{
    Task<UserAccess> GetAsync(Guid userId, CancellationToken ct = default);
}
