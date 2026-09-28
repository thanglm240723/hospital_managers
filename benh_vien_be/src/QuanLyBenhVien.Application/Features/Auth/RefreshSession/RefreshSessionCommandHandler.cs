using MediatR;
using QuanLyBenhVien.Application.Common.Results;
using QuanLyBenhVien.Application.Features.Auth.Common;

namespace QuanLyBenhVien.Application.Features.Auth.RefreshSession;

internal sealed class RefreshSessionCommandHandler : IRequestHandler<RefreshSessionCommand, Result<AuthTokensResult>>
{
    public Task<Result<AuthTokensResult>> Handle(RefreshSessionCommand request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
