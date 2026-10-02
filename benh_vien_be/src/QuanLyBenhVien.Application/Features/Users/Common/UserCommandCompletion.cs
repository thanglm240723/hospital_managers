using Microsoft.Extensions.Logging;
using QuanLyBenhVien.Application.Common.Caching;
using QuanLyBenhVien.Application.Common.Results;

namespace QuanLyBenhVien.Application.Features.Users.Common;

/// Phần sau commit dùng chung của các command tài khoản: flush invalidation (không ném) rồi đọc DTO từ projection.
internal static class UserCommandCompletion
{
    public static async Task<Result<UserDetailDto>> FlushAndReadAsync(
        ICacheInvalidator cacheInvalidator, IUsersReadService readService, ILogger logger, Guid userId, CancellationToken ct)
    {
        try
        {
            await cacheInvalidator.FlushAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Flush cache invalidation sau thay đổi tài khoản thất bại; worker sẽ xử lý lại.");
        }

        var dto = await readService.GetAsync(userId, CancellationToken.None);
        return dto is null ? UsersErrors.NotFound : dto;
    }
}
