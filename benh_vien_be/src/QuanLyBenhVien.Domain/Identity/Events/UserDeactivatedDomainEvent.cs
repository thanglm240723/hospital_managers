using QuanLyBenhVien.Domain.Common;

namespace QuanLyBenhVien.Domain.Identity.Events;

public sealed record UserDeactivatedDomainEvent(Guid UserId) : DomainEvent;
