using QuanLyBenhVien.Application.Common.Authorization;
using QuanLyBenhVien.Application.Common.Messaging;
using QuanLyBenhVien.Application.Common.Results;
using QuanLyBenhVien.Application.Features.Facilities.Common;

namespace QuanLyBenhVien.Application.Features.Facilities.GetFacilityTree;

public sealed record GetFacilityTreeQuery() : IQuery<Result<IReadOnlyList<BranchDto>>>, IUnscopedRequest;
