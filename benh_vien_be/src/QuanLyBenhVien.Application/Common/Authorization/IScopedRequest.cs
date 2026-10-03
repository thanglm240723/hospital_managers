namespace QuanLyBenhVien.Application.Common.Authorization;

/// Request chạm tài nguyên có lớp 2 (spec §7.4): handler PHẢI lọc/kiểm qua IAccessContext/IResourceAuthorizer.
public interface IScopedRequest;
