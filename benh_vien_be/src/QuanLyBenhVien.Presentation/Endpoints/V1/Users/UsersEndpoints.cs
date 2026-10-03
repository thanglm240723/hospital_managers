using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using QuanLyBenhVien.Application.Features.Users.ActivateUser;
using QuanLyBenhVien.Application.Features.Users.CreateUser;
using QuanLyBenhVien.Application.Features.Users.DeactivateUser;
using QuanLyBenhVien.Application.Features.Users.GetUser;
using QuanLyBenhVien.Application.Features.Users.GrantUserPermission;
using QuanLyBenhVien.Application.Features.Users.ListUsers;
using QuanLyBenhVien.Application.Features.Users.RevokeUserPermission;
using QuanLyBenhVien.Application.Features.Users.SetUserRoles;
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

        group.MapPost("/", CreateAsync)
            .RequirePermission(QuanLyBenhVien.Domain.Identity.Permissions.Users.Create)
            .RequireCsrf()
            .WithName("CreateUserV1");

        group.MapPost("/{id:guid}/activate", ActivateAsync)
            .RequirePermission(QuanLyBenhVien.Domain.Identity.Permissions.Users.Activate)
            .RequireCsrf()
            .WithName("ActivateUserV1");

        group.MapPost("/{id:guid}/deactivate", DeactivateAsync)
            .RequirePermission(QuanLyBenhVien.Domain.Identity.Permissions.Users.Activate)
            .RequireCsrf()
            .WithName("DeactivateUserV1");

        group.MapPut("/{id:guid}/roles", SetRolesAsync)
            .RequirePermission(QuanLyBenhVien.Domain.Identity.Permissions.Users.ManageRoles)
            .RequireCsrf()
            .WithName("SetUserRolesV1");

        group.MapPost("/{id:guid}/permissions/grant", GrantPermissionAsync)
            .RequirePermission(QuanLyBenhVien.Domain.Identity.Permissions.Users.ManagePermissions)
            .RequireCsrf()
            .WithName("GrantUserPermissionV1");

        group.MapPost("/{id:guid}/permissions/revoke", RevokePermissionAsync)
            .RequirePermission(QuanLyBenhVien.Domain.Identity.Permissions.Users.ManagePermissions)
            .RequireCsrf()
            .WithName("RevokeUserPermissionV1");
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

    private static async Task<IResult> CreateAsync(CreateUserRequest req, HttpContext http, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new CreateUserCommand(req.Email!, req.FullName!, req.RoleIds ?? []), ct);
        if (result.IsFailure) return result.Error!.ToProblem(http);

        // Response chứa mật khẩu ban đầu (plaintext, chỉ một lần) — cấm mọi cache.
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers.Pragma = "no-cache";
        return Results.Created($"/api/v1/users/{result.Value.User.Id}", result.Value);
    }

    private static async Task<IResult> ActivateAsync(Guid id, HttpContext http, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new ActivateUserCommand(id), ct);
        return result.IsFailure ? result.Error!.ToProblem(http) : Results.Ok(result.Value);
    }

    private static async Task<IResult> DeactivateAsync(Guid id, HttpContext http, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new DeactivateUserCommand(id), ct);
        return result.IsFailure ? result.Error!.ToProblem(http) : Results.Ok(result.Value);
    }

    private static async Task<IResult> SetRolesAsync(Guid id, SetUserRolesRequest req, HttpContext http, ISender sender, CancellationToken ct)
    {
        if (!TryReadIfMatch(http, out var version)) return InvalidIfMatch(http);
        var result = await sender.Send(new SetUserRolesCommand(id, req.RoleIds!, version), ct);
        return result.IsFailure ? result.Error!.ToProblem(http) : Results.Ok(result.Value);
    }

    private static async Task<IResult> GrantPermissionAsync(Guid id, PermissionGrantRequest req, HttpContext http, ISender sender, CancellationToken ct)
    {
        if (!TryReadIfMatch(http, out var version)) return InvalidIfMatch(http);
        var result = await sender.Send(new GrantUserPermissionCommand(id, req.PermissionCode!, req.Reason!, version), ct);
        return result.IsFailure ? result.Error!.ToProblem(http) : Results.Ok(result.Value);
    }

    private static async Task<IResult> RevokePermissionAsync(Guid id, PermissionGrantRequest req, HttpContext http, ISender sender, CancellationToken ct)
    {
        if (!TryReadIfMatch(http, out var version)) return InvalidIfMatch(http);
        var result = await sender.Send(new RevokeUserPermissionCommand(id, req.PermissionCode!, req.Reason!, version), ct);
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
            "Thiếu hoặc sai định dạng header If-Match (phiên bản tài khoản).");
}
