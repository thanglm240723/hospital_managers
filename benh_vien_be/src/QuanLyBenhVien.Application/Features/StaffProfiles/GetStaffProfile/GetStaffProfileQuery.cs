using QuanLyBenhVien.Application.Common.Authorization;
using QuanLyBenhVien.Application.Common.Messaging;
using QuanLyBenhVien.Application.Common.Results;
using QuanLyBenhVien.Application.Features.StaffProfiles.Common;

namespace QuanLyBenhVien.Application.Features.StaffProfiles.GetStaffProfile;

public sealed record GetStaffProfileQuery(Guid UserId) : IQuery<Result<StaffProfileDto>>, IUnscopedRequest;
