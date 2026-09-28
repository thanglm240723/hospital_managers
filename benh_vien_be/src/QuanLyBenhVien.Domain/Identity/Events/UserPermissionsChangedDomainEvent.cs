using QuanLyBenhVien.Domain.Common;

namespace QuanLyBenhVien.Domain.Identity.Events;

public sealed record UserPermissionsChangedDomainEvent(Guid UserId) : DomainEvent;
