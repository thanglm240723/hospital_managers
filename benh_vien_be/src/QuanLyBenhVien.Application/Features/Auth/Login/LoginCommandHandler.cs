using MediatR;
using QuanLyBenhVien.Application.Common.Results;
using QuanLyBenhVien.Application.Features.Auth.Common;

namespace QuanLyBenhVien.Application.Features.Auth.Login;

internal sealed class LoginCommandHandler : IRequestHandler<LoginCommand, Result<AuthTokensResult>>
{
    public Task<Result<AuthTokensResult>> Handle(LoginCommand request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
