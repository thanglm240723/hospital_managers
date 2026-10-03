using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using QuanLyBenhVien.Application.Features.Facilities.CreateBranch;
using QuanLyBenhVien.Application.Features.Facilities.CreateDepartment;
using QuanLyBenhVien.Application.Features.Facilities.CreateRoom;
using QuanLyBenhVien.Application.Features.Facilities.GetFacilityTree;
using QuanLyBenhVien.Application.Features.Facilities.UpdateBranch;
using QuanLyBenhVien.Application.Features.Facilities.UpdateDepartment;
using QuanLyBenhVien.Application.Features.Facilities.UpdateRoom;
using QuanLyBenhVien.Presentation.Http;
using QuanLyBenhVien.Presentation.Security;
using Perm = QuanLyBenhVien.Domain.Identity.Permissions;

namespace QuanLyBenhVien.Presentation.Endpoints.V1.Facilities;

public sealed class FacilitiesEndpoints : ICarterModule
{
    private const string Label = "cơ cấu tổ chức";

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/facilities").WithTags("Facilities");

        group.MapGet("/", GetTreeAsync)
            .RequirePermission(Perm.Facilities.Read)
            .WithName("GetFacilityTreeV1");

        group.MapPost("/branches", CreateBranchAsync)
            .RequirePermission(Perm.Facilities.Manage).RequireCsrf().WithName("CreateBranchV1");
        group.MapPost("/departments", CreateDepartmentAsync)
            .RequirePermission(Perm.Facilities.Manage).RequireCsrf().WithName("CreateDepartmentV1");
        group.MapPost("/rooms", CreateRoomAsync)
            .RequirePermission(Perm.Facilities.Manage).RequireCsrf().WithName("CreateRoomV1");

        group.MapPut("/branches/{id:guid}", UpdateBranchAsync)
            .RequirePermission(Perm.Facilities.Manage).RequireCsrf().WithName("UpdateBranchV1");
        group.MapPut("/departments/{id:guid}", UpdateDepartmentAsync)
            .RequirePermission(Perm.Facilities.Manage).RequireCsrf().WithName("UpdateDepartmentV1");
        group.MapPut("/rooms/{id:guid}", UpdateRoomAsync)
            .RequirePermission(Perm.Facilities.Manage).RequireCsrf().WithName("UpdateRoomV1");
    }

    private static async Task<IResult> GetTreeAsync(HttpContext http, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new GetFacilityTreeQuery(), ct);
        return result.IsFailure ? result.Error!.ToProblem(http) : Results.Ok(result.Value);
    }

    private static async Task<IResult> CreateBranchAsync(CreateBranchRequest req, HttpContext http, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new CreateBranchCommand(req.Code, req.Name), ct);
        return result.IsFailure ? result.Error!.ToProblem(http) : Results.Created($"/api/v1/facilities/branches/{result.Value.Id}", result.Value);
    }

    private static async Task<IResult> CreateDepartmentAsync(CreateDepartmentRequest req, HttpContext http, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new CreateDepartmentCommand(req.BranchId, req.Code, req.Name, req.Kind), ct);
        return result.IsFailure ? result.Error!.ToProblem(http) : Results.Created($"/api/v1/facilities/departments/{result.Value.Id}", result.Value);
    }

    private static async Task<IResult> CreateRoomAsync(CreateRoomRequest req, HttpContext http, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new CreateRoomCommand(req.DepartmentId, req.Code, req.Name), ct);
        return result.IsFailure ? result.Error!.ToProblem(http) : Results.Created($"/api/v1/facilities/rooms/{result.Value.Id}", result.Value);
    }

    private static async Task<IResult> UpdateBranchAsync(Guid id, UpdateFacilityRequest req, HttpContext http, ISender sender, CancellationToken ct)
    {
        if (!IfMatch.TryRead(http, out var version)) return IfMatch.Invalid(http, Label);
        var result = await sender.Send(new UpdateBranchCommand(id, req.Name, req.IsActive, version), ct);
        return result.IsFailure ? result.Error!.ToProblem(http) : Results.Ok(result.Value);
    }

    private static async Task<IResult> UpdateDepartmentAsync(Guid id, UpdateFacilityRequest req, HttpContext http, ISender sender, CancellationToken ct)
    {
        if (!IfMatch.TryRead(http, out var version)) return IfMatch.Invalid(http, Label);
        var result = await sender.Send(new UpdateDepartmentCommand(id, req.Name, req.IsActive, version), ct);
        return result.IsFailure ? result.Error!.ToProblem(http) : Results.Ok(result.Value);
    }

    private static async Task<IResult> UpdateRoomAsync(Guid id, UpdateFacilityRequest req, HttpContext http, ISender sender, CancellationToken ct)
    {
        if (!IfMatch.TryRead(http, out var version)) return IfMatch.Invalid(http, Label);
        var result = await sender.Send(new UpdateRoomCommand(id, req.Name, req.IsActive, version), ct);
        return result.IsFailure ? result.Error!.ToProblem(http) : Results.Ok(result.Value);
    }
}
