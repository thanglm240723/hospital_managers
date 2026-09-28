using MediatR;
using QuanLyBenhVien.Application.Common.Results;

namespace QuanLyBenhVien.Application.Features.Auth.GetMe;

internal sealed class GetMeQueryHandler : IRequestHandler<GetMeQuery, Result<MeDto>>
{
    public Task<Result<MeDto>> Handle(GetMeQuery request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
