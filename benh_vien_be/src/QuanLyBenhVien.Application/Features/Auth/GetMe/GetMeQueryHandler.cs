using MediatR;
using QuanLyBenhVien.Application.Common.Identity;
using QuanLyBenhVien.Application.Common.Results;
using QuanLyBenhVien.Application.Features.Auth.Common;

namespace QuanLyBenhVien.Application.Features.Auth.GetMe;

internal sealed class GetMeQueryHandler(ICurrentUser currentUser, IAuthReadService authRead)
    : IRequestHandler<GetMeQuery, Result<MeDto>>
{
    public async Task<Result<MeDto>> Handle(GetMeQuery request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return AuthErrors.Unauthenticated;
        }

        var me = await authRead.GetMeAsync(userId, cancellationToken);
        return me is null ? AuthErrors.Unauthenticated : me;
    }
}
