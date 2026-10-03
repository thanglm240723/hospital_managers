using QuanLyBenhVien.Application.Common.Authorization;
using QuanLyBenhVien.Application.Common.Messaging;
using QuanLyBenhVien.Application.Common.Results;
using QuanLyBenhVien.Application.Features.Facilities.Common;

namespace QuanLyBenhVien.Application.Features.Facilities.CreateDepartment;

public sealed record CreateDepartmentCommand(Guid BranchId, string Code, string Name, string Kind) : ICommand<Result<DepartmentDto>>, IUnscopedRequest;
