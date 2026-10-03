using FluentValidation;

namespace QuanLyBenhVien.Application.Features.StaffProfiles.SetStaffWorkScopes;

internal sealed class SetStaffWorkScopesCommandValidator : AbstractValidator<SetStaffWorkScopesCommand>
{
    public SetStaffWorkScopesCommandValidator()
    {
        RuleFor(x => x.DepartmentIds).NotNull().WithMessage("Vui lòng gửi danh sách khoa.")
            .Must(ids => ids is null || ids.Count <= 100).WithMessage("Tối đa 100 khoa.");
    }
}
