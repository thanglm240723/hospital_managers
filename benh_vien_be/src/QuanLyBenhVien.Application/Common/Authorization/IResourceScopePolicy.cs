namespace QuanLyBenhVien.Application.Common.Authorization;

/// Module đăng ký một policy cho mỗi cặp (PermissionCode, ResourceType). Implement ở Persistence của module.
public interface IResourceScopePolicy
{
    string PermissionCode { get; }
    string ResourceType { get; }
    Task<AccessDecision> EvaluateAsync(AccessScope scope, Guid resourceId, DateTimeOffset now, CancellationToken ct);
}
