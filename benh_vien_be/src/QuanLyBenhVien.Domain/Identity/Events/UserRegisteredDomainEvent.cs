using QuanLyBenhVien.Domain.Common;

namespace QuanLyBenhVien.Domain.Identity.Events;

public sealed record UserRegisteredDomainEvent(Guid UserId, string Email) : DomainEvent;
