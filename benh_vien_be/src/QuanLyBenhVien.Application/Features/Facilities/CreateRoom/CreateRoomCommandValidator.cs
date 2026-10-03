using FluentValidation;
using QuanLyBenhVien.Application.Features.Facilities.Common;

namespace QuanLyBenhVien.Application.Features.Facilities.CreateRoom;

internal sealed class CreateRoomCommandValidator : AbstractValidator<CreateRoomCommand>
{
    public CreateRoomCommandValidator()
    {
        RuleFor(x => x.DepartmentId).NotEmpty().WithMessage("Vui lòng chọn khoa.");
        RuleFor(x => x.Code).FacilityCode();
        RuleFor(x => x.Name).FacilityName();
    }
}
