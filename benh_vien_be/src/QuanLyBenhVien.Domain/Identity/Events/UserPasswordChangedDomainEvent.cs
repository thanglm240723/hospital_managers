using QuanLyBenhVien.Domain.Common;

namespace QuanLyBenhVien.Domain.Identity.Events;

public sealed record UserPasswordChangedDomainEvent(Guid UserId) : DomainEvent;
