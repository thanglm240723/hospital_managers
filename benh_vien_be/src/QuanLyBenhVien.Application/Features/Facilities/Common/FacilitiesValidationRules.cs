using FluentValidation;

namespace QuanLyBenhVien.Application.Features.Facilities.Common;

internal static class FacilitiesValidationRules
{
    public static IRuleBuilderOptions<T, string> FacilityCode<T>(this IRuleBuilder<T, string> rule)
        => rule.NotEmpty().WithMessage("Vui lòng nhập mã.")
            .Matches(@"^[A-Z0-9][A-Z0-9-]{0,29}\z").WithMessage("Mã gồm 1-30 ký tự A-Z, 0-9 hoặc '-', bắt đầu bằng chữ hoặc số.");

    public static IRuleBuilderOptions<T, string> FacilityName<T>(this IRuleBuilder<T, string> rule)
        => rule.NotEmpty().WithMessage("Vui lòng nhập tên.")
            .Must(n => n is null || n.Trim().Length <= 200).WithMessage("Tên tối đa 200 ký tự.");

    public static IRuleBuilderOptions<T, string> FacilityKind<T>(this IRuleBuilder<T, string> rule)
        => rule.Must(k => FacilitiesErrors.TryParseKind(k, out _))
            .WithMessage("Loại khoa phải là clinical, laboratory, pharmacy, billing hoặc administrative.");
}
