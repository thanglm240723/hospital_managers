using MediatR;
using QuanLyBenhVien.Application.Common.Results;
using QuanLyBenhVien.Application.Features.Users.Common;

namespace QuanLyBenhVien.Application.Features.Users.GetUser;

internal sealed class GetUserQueryHandler(IUsersReadService read)
    : IRequestHandler<GetUserQuery, Result<UserDetailDto>>
{
    public async Task<Result<UserDetailDto>> Handle(GetUserQuery request, CancellationToken cancellationToken)
    {
        var user = await read.GetAsync(request.Id, cancellationToken);
        return user is null ? Result.Failure<UserDetailDto>(UsersErrors.NotFound) : Result.Success(user);
    }
}
