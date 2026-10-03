namespace QuanLyBenhVien.Application.Common.Authorization;

public interface IDeniedAccessRecorder
{
    Task RecordAsync(string action, ResourceRef resource, string reason, CancellationToken ct);
}
