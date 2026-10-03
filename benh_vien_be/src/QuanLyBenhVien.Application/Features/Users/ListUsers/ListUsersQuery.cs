using QuanLyBenhVien.Application.Common.Messaging;
using QuanLyBenhVien.Application.Common.Models;
using QuanLyBenhVien.Application.Common.Results;
using QuanLyBenhVien.Application.Features.Users.Common;

namespace QuanLyBenhVien.Application.Features.Users.ListUsers;

/// Status: active | must_change_password | locked.
public sealed record ListUsersQuery(
    int PageNumber = 1,
    int PageSize = 20,
    string? SearchTerm = null,
    Guid? RoleId = null,
    string? Status = null) : IQuery<Result<PagedResult<UserSummaryDto>>>;
