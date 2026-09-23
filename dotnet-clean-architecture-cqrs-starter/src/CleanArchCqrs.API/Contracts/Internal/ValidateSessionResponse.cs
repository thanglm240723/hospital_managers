namespace CleanArchCqrs.API.Contracts.Internal;

/// absExp: Unix seconds — cùng định dạng với giá trị session:{fid} Gateway đọc từ Redis.
public sealed record ValidateSessionResponse(bool Valid, Guid? UserId, long? AbsExp);
