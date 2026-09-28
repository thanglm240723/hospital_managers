using QuanLyBenhVien.Application.Features.Auth.GetMe;

namespace QuanLyBenhVien.Application.Features.Auth.Common;

public interface IAuthReadService
{
    Task<SessionStateDto?> GetSessionStateAsync(Guid sessionFamilyId, CancellationToken ct = default);

    Task<MeDto?> GetMeAsync(Guid userId, CancellationToken ct = default);
}
