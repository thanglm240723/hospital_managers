using FluentValidation;

namespace QuanLyBenhVien.Application.Features.StaffProfiles.UpsertStaffProfile;

internal sealed class UpsertStaffProfileCommandValidator : AbstractValidator<UpsertStaffProfileCommand>
{
    public UpsertStaffProfileCommandValidator()
    {
        RuleFor(x => x.StaffCode).NotEmpty().WithMessage("Vui lòng nhập mã nhân sự.")
            .Matches(@"^[A-Z0-9][A-Z0-9-]{1,29}\z").WithMessage("Mã nhân sự gồm 2-30 ký tự A-Z, 0-9 hoặc dấu gạch ngang, bắt đầu bằng chữ hoặc số.");
    }
}
