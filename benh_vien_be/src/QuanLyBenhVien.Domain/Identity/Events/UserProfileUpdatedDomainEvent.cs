using QuanLyBenhVien.Domain.Common;

namespace QuanLyBenhVien.Domain.Identity.Events;

public sealed record UserProfileUpdatedDomainEvent(Guid UserId) : DomainEvent;
