using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using QuanLyBenhVien.Application.Features.Roles.CreateRole;
using QuanLyBenhVien.Application.Features.Roles.ListRoles;
using QuanLyBenhVien.Application.Features.Roles.RenameRole;
using QuanLyBenhVien.Application.Features.Roles.SetRolePermissions;
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

        group.MapPost("/", CreateAsync)
            .RequirePermission(QuanLyBenhVien.Domain.Identity.Permissions.Roles.Manage)
            .RequireCsrf()
            .WithName("CreateRoleV1");

        group.MapPut("/{id:guid}", RenameAsync)
            .RequirePermission(QuanLyBenhVien.Domain.Identity.Permissions.Roles.Manage)
            .RequireCsrf()
            .WithName("RenameRoleV1");

        group.MapPut("/{id:guid}/permissions", SetPermissionsAsync)
            .RequirePermission(QuanLyBenhVien.Domain.Identity.Permissions.Roles.Manage)
            .RequireCsrf()
            .WithName("SetRolePermissionsV1");
    }

    private static async Task<IResult> ListAsync(HttpContext http, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new ListRolesQuery(), ct);
        return result.IsFailure ? result.Error!.ToProblem(http) : Results.Ok(result.Value);
    }

    private static async Task<IResult> CreateAsync(CreateRoleRequest req, HttpContext http, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new CreateRoleCommand(req.Code, req.Name, req.PermissionCodes ?? []), ct);
        return result.IsFailure ? result.Error!.ToProblem(http) : Results.Created($"/api/v1/roles/{result.Value.Id}", result.Value);
    }

    private static async Task<IResult> RenameAsync(Guid id, RenameRoleRequest req, HttpContext http, ISender sender, CancellationToken ct)
    {
        if (!TryReadIfMatch(http, out var version)) return InvalidIfMatch(http);
        var result = await sender.Send(new RenameRoleCommand(id, req.Name, version), ct);
        return result.IsFailure ? result.Error!.ToProblem(http) : Results.Ok(result.Value);
    }

    private static async Task<IResult> SetPermissionsAsync(Guid id, SetRolePermissionsRequest req, HttpContext http, ISender sender, CancellationToken ct)
    {
        if (!TryReadIfMatch(http, out var version)) return InvalidIfMatch(http);
        var result = await sender.Send(new SetRolePermissionsCommand(id, req.PermissionCodes!, version), ct);
        return result.IsFailure ? result.Error!.ToProblem(http) : Results.Ok(result.Value);
    }

    /// If-Match chứa RowVersion (số nguyên không dấu), có thể bọc dấu nháy kép.
    private static bool TryReadIfMatch(HttpContext http, out uint version)
    {
        version = 0;
        var raw = http.Request.Headers.IfMatch.ToString().Trim().Trim('"');
        return uint.TryParse(raw, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out version);
    }

    private static IResult InvalidIfMatch(HttpContext http)
        => ProblemResponses.Create(http, StatusCodes.Status400BadRequest, "invalid_if_match",
            "Thiếu hoặc sai định dạng header If-Match (phiên bản vai trò).");
}
