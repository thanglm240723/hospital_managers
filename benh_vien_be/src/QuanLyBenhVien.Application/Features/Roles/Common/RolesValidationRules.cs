using FluentValidation;
using QuanLyBenhVien.Domain.Identity;

namespace QuanLyBenhVien.Application.Features.Roles.Common;

internal static class RolesValidationRules
{
    public static IRuleBuilderOptions<T, string> RolesName<T>(this IRuleBuilder<T, string> rule)
        => rule.NotEmpty().WithMessage("Vui lòng nhập tên vai trò.")
            .MaximumLength(100).WithMessage("Tên vai trò tối đa 100 ký tự.");

    public static IRuleBuilderOptions<T, IReadOnlyList<string>> RolesPermissionCodes<T>(this IRuleBuilder<T, IReadOnlyList<string>> rule)
        => rule.NotNull().WithMessage("Vui lòng cung cấp danh sách quyền.")
            .Must(codes => codes is null || codes.All(c => c is not null && QuanLyBenhVien.Domain.Identity.Permissions.IsDefined(c)))
            .WithMessage("Danh sách quyền chứa mã không tồn tại trong danh mục.");
}
