namespace QuanLyBenhVien.Application.Features.Auth.Common;

public interface IAccessTokenIssuer
{
    AccessToken Issue(Guid userId, Guid sessionFamilyId, int securityVersion);
}
