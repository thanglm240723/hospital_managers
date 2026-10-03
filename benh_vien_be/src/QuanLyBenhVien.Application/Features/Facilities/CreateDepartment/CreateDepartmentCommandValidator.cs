using FluentValidation;
using QuanLyBenhVien.Application.Features.Facilities.Common;

namespace QuanLyBenhVien.Application.Features.Facilities.CreateDepartment;

internal sealed class CreateDepartmentCommandValidator : AbstractValidator<CreateDepartmentCommand>
{
    public CreateDepartmentCommandValidator()
    {
        RuleFor(x => x.BranchId).NotEmpty().WithMessage("Vui lòng chọn cơ sở.");
        RuleFor(x => x.Code).FacilityCode();
        RuleFor(x => x.Name).FacilityName();
        RuleFor(x => x.Kind).FacilityKind();
    }
}
