using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using QuanLyBenhVien.Application.Features.Roles.ListRoles;
using QuanLyBenhVien.Presentation.Http;
using QuanLyBenhVien.Presentation.Security;

namespace QuanLyBenhVien.Presentation.Endpoints.V1.Roles;

public sealed class RolesEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/roles").WithTags("Roles");

        group.MapGet("/", ListAsync)
            .RequirePermission(QuanLyBenhVien.Domain.Identity.Permissions.Roles.Read)
            .WithName("ListRolesV1");
    }

    private static async Task<IResult> ListAsync(HttpContext http, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new ListRolesQuery(), ct);
        return result.IsFailure ? result.Error!.ToProblem(http) : Results.Ok(result.Value);
    }
}
