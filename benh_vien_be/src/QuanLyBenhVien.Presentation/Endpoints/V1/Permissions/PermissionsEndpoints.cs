using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using QuanLyBenhVien.Application.Features.Permissions.ListPermissions;
using QuanLyBenhVien.Presentation.Http;
using QuanLyBenhVien.Presentation.Security;

namespace QuanLyBenhVien.Presentation.Endpoints.V1.Permissions;

public sealed class PermissionsEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/permissions").WithTags("Permissions");

        group.MapGet("/", ListAsync)
            .RequirePermission(QuanLyBenhVien.Domain.Identity.Permissions.Catalog.Read)
            .WithName("ListPermissionsV1");
    }

    private static async Task<IResult> ListAsync(HttpContext http, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new ListPermissionsQuery(), ct);
        return result.IsFailure ? result.Error!.ToProblem(http) : Results.Ok(result.Value);
    }
}
