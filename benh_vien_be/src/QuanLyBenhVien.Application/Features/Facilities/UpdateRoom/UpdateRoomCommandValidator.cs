using FluentValidation;
using QuanLyBenhVien.Application.Features.Facilities.Common;

namespace QuanLyBenhVien.Application.Features.Facilities.UpdateRoom;

internal sealed class UpdateRoomCommandValidator : AbstractValidator<UpdateRoomCommand>
{
    public UpdateRoomCommandValidator()
    {
        RuleFor(x => x.Name).FacilityName();
    }
}
