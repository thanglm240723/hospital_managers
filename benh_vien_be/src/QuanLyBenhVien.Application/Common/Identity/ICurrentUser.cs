namespace QuanLyBenhVien.Application.Common.Identity;

public interface ICurrentUser
{
    Guid? UserId { get; }
    Guid? SessionFamilyId { get; }
}
