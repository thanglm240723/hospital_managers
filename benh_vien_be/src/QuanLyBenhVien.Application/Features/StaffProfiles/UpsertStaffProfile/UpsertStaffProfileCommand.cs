using QuanLyBenhVien.Application.Common.Authorization;
using QuanLyBenhVien.Application.Common.Messaging;
using QuanLyBenhVien.Application.Common.Results;
using QuanLyBenhVien.Application.Features.StaffProfiles.Common;

namespace QuanLyBenhVien.Application.Features.StaffProfiles.UpsertStaffProfile;

/// ExpectedVersion null = tạo mới (chưa có hồ sơ); có giá trị = cập nhật hồ sơ có sẵn.
public sealed record UpsertStaffProfileCommand(Guid UserId, string StaffCode, bool IsActive, uint? ExpectedVersion)
    : ICommand<Result<StaffProfileDto>>, IUnscopedRequest;
