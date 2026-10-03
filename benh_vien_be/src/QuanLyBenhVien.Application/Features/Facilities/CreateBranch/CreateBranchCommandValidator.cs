using FluentValidation;
using QuanLyBenhVien.Application.Features.Facilities.Common;

namespace QuanLyBenhVien.Application.Features.Facilities.CreateBranch;

internal sealed class CreateBranchCommandValidator : AbstractValidator<CreateBranchCommand>
{
    public CreateBranchCommandValidator()
    {
        RuleFor(x => x.Code).FacilityCode();
        RuleFor(x => x.Name).FacilityName();
    }
}
