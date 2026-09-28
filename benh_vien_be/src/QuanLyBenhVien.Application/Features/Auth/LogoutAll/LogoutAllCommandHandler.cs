using MediatR;
using QuanLyBenhVien.Application.Common.Results;

namespace QuanLyBenhVien.Application.Features.Auth.LogoutAll;

internal sealed class LogoutAllCommandHandler : IRequestHandler<LogoutAllCommand, Result>
{
    public Task<Result> Handle(LogoutAllCommand request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
