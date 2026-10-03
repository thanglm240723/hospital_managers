namespace QuanLyBenhVien.Application.Common.Authorization;

public interface IResourceAuthorizer
{
    Task<AccessDecision> AuthorizeAsync(string permissionCode, ResourceRef resource, CancellationToken ct);
}
