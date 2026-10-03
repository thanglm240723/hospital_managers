namespace QuanLyBenhVien.Application.Common.Authorization;

public interface IAccessContext
{
    Task<AccessScope> GetAsync(CancellationToken ct);
}
