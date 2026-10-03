using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using QuanLyBenhVien.Application.Features.StaffProfiles.GetStaffProfile;
using QuanLyBenhVien.Application.Features.StaffProfiles.SetStaffWorkScopes;
using QuanLyBenhVien.Application.Features.StaffProfiles.UpsertStaffProfile;
using QuanLyBenhVien.Presentation.Http;
using QuanLyBenhVien.Presentation.Security;
using Perm = QuanLyBenhVien.Domain.Identity.Permissions;

namespace QuanLyBenhVien.Presentation.Endpoints.V1.StaffProfiles;

public sealed class StaffProfilesEndpoints : ICarterModule
{
    private const string Label = "hồ sơ nhân sự";

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/users/{userId:guid}/staff-profile").WithTags("StaffProfiles");

        group.MapGet("", GetAsync)
            .RequirePermission(Perm.StaffProfiles.Read).WithName("GetStaffProfileV1");
        group.MapPut("", UpsertAsync)
            .RequirePermission(Perm.StaffProfiles.Manage).RequireCsrf().WithName("UpsertStaffProfileV1");
        group.MapPut("/work-scopes", SetWorkScopesAsync)
            .RequirePermission(Perm.StaffProfiles.Manage).RequireCsrf().WithName("SetStaffWorkScopesV1");
    }

    private static async Task<IResult> GetAsync(Guid userId, HttpContext http, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new GetStaffProfileQuery(userId), ct);
        return result.IsFailure ? result.Error!.ToProblem(http) : Results.Ok(result.Value);
    }

    private static async Task<IResult> UpsertAsync(Guid userId, UpsertStaffProfileRequest req, HttpContext http, ISender sender, CancellationToken ct)
    {
        // Không có If-Match => tạo mới; có => cập nhật (sai định dạng vẫn là 400).
        uint? expected = null;
        if (http.Request.Headers.ContainsKey("If-Match"))
        {
            if (!IfMatch.TryRead(http, out var version)) return IfMatch.Invalid(http, Label);
            expected = version;
        }

        var result = await sender.Send(new UpsertStaffProfileCommand(userId, req.StaffCode, req.IsActive, expected), ct);
        if (result.IsFailure) return result.Error!.ToProblem(http);
        return expected is null
            ? Results.Created($"/api/v1/users/{userId}/staff-profile", result.Value)
            : Results.Ok(result.Value);
    }

    private static async Task<IResult> SetWorkScopesAsync(Guid userId, SetStaffWorkScopesRequest req, HttpContext http, ISender sender, CancellationToken ct)
    {
        if (!IfMatch.TryRead(http, out var version)) return IfMatch.Invalid(http, Label);
        var result = await sender.Send(new SetStaffWorkScopesCommand(userId, req.DepartmentIds ?? [], version), ct);
        return result.IsFailure ? result.Error!.ToProblem(http) : Results.Ok(result.Value);
    }
}
