namespace CleanArchCqrs.Application.Common.Interfaces
{
    //⚠ Application cần biết "ai đang thao tác" nhưng **không được** `using Microsoft.AspNetCore.Http`.
    //Application không nên lấy hết qua http mà chỉ lấy 1 số thông tin cần thiết về user đang thao tác.
    public interface ICurrentUser
    {
        Guid? UserId { get; }
        string? Email { get; }
        string? Role { get; }
        bool IsAuthenticated { get; }

    }
}