using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using QuanLyBenhVien.Application.Features.Users.GetUser;
using QuanLyBenhVien.Application.Features.Users.ListUsers;
using QuanLyBenhVien.Presentation.Http;
using QuanLyBenhVien.Presentation.Security;

namespace QuanLyBenhVien.Presentation.Endpoints.V1.Users;

public sealed class UsersEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/users").WithTags("Users");

        group.MapGet("/", ListAsync)
            .RequirePermission(QuanLyBenhVien.Domain.Identity.Permissions.Users.Read)
            .WithName("ListUsersV1");

        group.MapGet("/{id:guid}", GetAsync)
            .RequirePermission(QuanLyBenhVien.Domain.Identity.Permissions.Users.Read)
            .WithName("GetUserV1");
    }

    private static async Task<IResult> ListAsync(
        HttpContext http, ISender sender, CancellationToken ct,
        int pageNumber = 1, int pageSize = 20, string? searchTerm = null, Guid? roleId = null, string? status = null)
    {
        var result = await sender.Send(new ListUsersQuery(pageNumber, pageSize, searchTerm, roleId, status), ct);
        return result.IsFailure ? result.Error!.ToProblem(http) : Results.Ok(result.Value);
    }

    private static async Task<IResult> GetAsync(Guid id, HttpContext http, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new GetUserQuery(id), ct);
        return result.IsFailure ? result.Error!.ToProblem(http) : Results.Ok(result.Value);
    }
}
