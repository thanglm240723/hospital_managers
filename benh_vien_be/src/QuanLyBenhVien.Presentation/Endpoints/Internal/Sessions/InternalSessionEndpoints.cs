using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using QuanLyBenhVien.Application.Features.Auth.ValidateSession;
using QuanLyBenhVien.Presentation.Http;

namespace QuanLyBenhVien.Presentation.Endpoints.Internal.Sessions;

/// Chỉ Gateway gọi (`Gateway/Auth/SessionValidator.cs`) khi Redis `session:{fid}` miss/lỗi. Gateway không lộ
/// route `/internal/*` ra ngoài; `InternalApiKeyFilter` là lớp phòng thủ thứ hai.
public sealed class InternalSessionEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/internal/sessions");

        group.MapPost("/validate", ValidateAsync)
            .AllowAnonymous()
            .AddEndpointFilter<InternalApiKeyFilter>()
            .ExcludeFromDescription()
            .WithName("ValidateSessionInternal");
    }

    private static async Task<IResult> ValidateAsync(ValidateSessionRequest req, HttpContext http, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new ValidateSessionQuery(req.FamilyId, req.Sv), ct);
        return result.IsFailure ? result.Error!.ToProblem(http) : Results.Ok(result.Value);
    }
}
