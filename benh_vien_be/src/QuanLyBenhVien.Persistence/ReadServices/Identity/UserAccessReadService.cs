using QuanLyBenhVien.Application.Common.Identity;

namespace QuanLyBenhVien.Persistence.ReadServices.Identity;

internal sealed class UserAccessReadService(AppDbContext db) : IUserAccessReadService
{
    public Task<UserAccessDto?> GetAsync(Guid userId, CancellationToken ct)
        => EffectivePermissions.LoadAsync(db, userId, ct);
}
