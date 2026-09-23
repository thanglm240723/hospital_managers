namespace CleanArchCqrs.Application.Common.Interfaces
{
    //⚠ Application cần biết "ai đang thao tác" nhưng **không được** `using Microsoft.AspNetCore.Http`.
    //JWT chỉ mang định danh (sub, fid, sv) — role/permission lấy qua IPermissionService, không qua đây.
    public interface ICurrentUser
    {
        Guid? UserId { get; }
        Guid? SessionFamilyId { get; }
        bool IsAuthenticated { get; }
    }
}
