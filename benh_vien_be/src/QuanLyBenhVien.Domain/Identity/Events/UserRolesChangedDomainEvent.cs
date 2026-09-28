using QuanLyBenhVien.Domain.Common;

namespace QuanLyBenhVien.Domain.Identity.Events;

public sealed record UserRolesChangedDomainEvent(Guid UserId) : DomainEvent;
