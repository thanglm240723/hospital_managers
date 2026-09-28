using QuanLyBenhVien.Application.Common.Messaging;
using QuanLyBenhVien.Application.Common.Results;

namespace QuanLyBenhVien.Application.Features.Auth.ValidateSession;

public sealed record ValidateSessionQuery(Guid SessionFamilyId, int SecurityVersion) : IQuery<Result<SessionValidationDto>>;
