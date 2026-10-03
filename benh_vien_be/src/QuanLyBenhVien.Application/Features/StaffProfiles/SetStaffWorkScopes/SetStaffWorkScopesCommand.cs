using QuanLyBenhVien.Application.Common.Authorization;
using QuanLyBenhVien.Application.Common.Messaging;
using QuanLyBenhVien.Application.Common.Results;
using QuanLyBenhVien.Application.Features.StaffProfiles.Common;

namespace QuanLyBenhVien.Application.Features.StaffProfiles.SetStaffWorkScopes;

public sealed record SetStaffWorkScopesCommand(Guid UserId, IReadOnlyList<Guid> DepartmentIds, uint ExpectedVersion)
    : ICommand<Result<StaffProfileDto>>, IUnscopedRequest;
