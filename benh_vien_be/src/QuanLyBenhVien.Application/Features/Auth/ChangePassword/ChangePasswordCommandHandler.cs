using MediatR;
using QuanLyBenhVien.Application.Common.Results;
using QuanLyBenhVien.Application.Features.Auth.Common;

namespace QuanLyBenhVien.Application.Features.Auth.ChangePassword;

internal sealed class ChangePasswordCommandHandler : IRequestHandler<ChangePasswordCommand, Result<AuthTokensResult>>
{
    public Task<Result<AuthTokensResult>> Handle(ChangePasswordCommand request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
