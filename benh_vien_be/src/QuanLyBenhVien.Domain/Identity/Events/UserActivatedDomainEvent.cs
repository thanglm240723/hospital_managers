using QuanLyBenhVien.Domain.Common;

namespace QuanLyBenhVien.Domain.Identity.Events;

public sealed record UserActivatedDomainEvent(Guid UserId) : DomainEvent;
