using MediatR;
using QuanLyBenhVien.Application.Common.Results;
using QuanLyBenhVien.Application.Features.Auth.Common;

namespace QuanLyBenhVien.Application.Features.Auth.ChangePassword;

internal sealed class ChangePasswordCommandHandler : IRequestHandler<ChangePasswordCommand, Result<AccessTokenDto>>
{
    public Task<Result<AccessTokenDto>> Handle(ChangePasswordCommand request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
