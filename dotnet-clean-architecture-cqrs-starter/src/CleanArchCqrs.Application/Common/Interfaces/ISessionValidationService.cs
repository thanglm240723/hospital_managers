using CleanArchCqrs.Application.Auth.Models;

namespace CleanArchCqrs.Application.Common.Interfaces;

/// Nguồn sự thật (DB) cho Gateway khi Redis không có session:{fid}. Hợp lệ thì nạp lại cache.
public interface ISessionValidationService
{
    Task<SessionValidationResult> ValidateAsync(Guid sessionFamilyId, int securityVersion, CancellationToken ct = default);
}
