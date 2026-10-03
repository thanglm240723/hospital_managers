using QuanLyBenhVien.Application.Common.Authorization;
using QuanLyBenhVien.Application.Common.Messaging;
using QuanLyBenhVien.Application.Common.Results;
using QuanLyBenhVien.Application.Features.Facilities.Common;

namespace QuanLyBenhVien.Application.Features.Facilities.CreateBranch;

public sealed record CreateBranchCommand(string Code, string Name) : ICommand<Result<BranchDto>>, IUnscopedRequest;
