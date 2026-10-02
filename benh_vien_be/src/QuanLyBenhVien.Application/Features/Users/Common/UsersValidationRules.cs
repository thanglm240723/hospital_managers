using FluentValidation;

namespace QuanLyBenhVien.Application.Features.Users.Common;

internal static class UsersValidationRules
{
    public static IRuleBuilderOptions<T, IReadOnlyList<Guid>> UsersRoleIds<T>(this IRuleBuilder<T, IReadOnlyList<Guid>> rule)
        => rule.NotNull().WithMessage("Vui lòng cung cấp danh sách vai trò.")
            .Must(ids => ids is null || (ids.All(id => id != Guid.Empty) && ids.Distinct().Count() == ids.Count))
            .WithMessage("Danh sách vai trò không được trùng hoặc rỗng Id.");

    public static IRuleBuilderOptions<T, string> UsersPermissionCode<T>(this IRuleBuilder<T, string> rule)
        => rule.NotEmpty().WithMessage("Vui lòng chọn quyền.")
            .Must(code => code is null || QuanLyBenhVien.Domain.Identity.Permissions.IsDefined(code))
            .WithMessage("Mã quyền không tồn tại trong danh mục.");

    public static IRuleBuilderOptions<T, string> UsersReason<T>(this IRuleBuilder<T, string> rule)
        => rule.Must(r => !string.IsNullOrWhiteSpace(r)).WithMessage("Vui lòng nhập lý do.")
            .MaximumLength(500).WithMessage("Lý do tối đa 500 ký tự.");
}
