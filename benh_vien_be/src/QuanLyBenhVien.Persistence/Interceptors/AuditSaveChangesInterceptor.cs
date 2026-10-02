using System.Text.Json;
using QuanLyBenhVien.Application.Common.Identity;
using QuanLyBenhVien.Domain.Common.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace QuanLyBenhVien.Persistence.Interceptors;

internal sealed class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    private static readonly HashSet<string> ExcludedProperties = new(StringComparer.Ordinal) { "PasswordHash", "TokenHash" };

    private readonly ICurrentUser _currentUser;
    private readonly IRequestContext _requestContext;

    public AuditSaveChangesInterceptor(ICurrentUser currentUser, IRequestContext requestContext)
    {
        _currentUser = currentUser;
        _requestContext = requestContext;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        AddAuditEntries(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        AddAuditEntries(eventData.Context);
        return base.SavingChangesAsync(eventData, result, ct);
    }

    private void AddAuditEntries(DbContext? context)
    {
        if (context is null) return;

        foreach (var entry in context.ChangeTracker.Entries()
            .Where(e => e.Entity is IAuditable
                     && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList())
        {
            var changes = new Dictionary<string, object?>();
            var action = entry.State switch
            {
                EntityState.Added => AuditAction.Created,
                EntityState.Modified => AuditAction.Updated,
                _ => AuditAction.Deleted
            };

            foreach (var p in entry.Properties.Where(p => !ExcludedProperties.Contains(p.Metadata.Name) && !p.Metadata.IsConcurrencyToken))
            {
                switch (action)
                {
                    case AuditAction.Created:
                        changes[p.Metadata.Name] = new { New = p.CurrentValue };
                        break;
                    case AuditAction.Updated when p.IsModified && !Equals(p.OriginalValue, p.CurrentValue):
                        changes[p.Metadata.Name] = new { Old = p.OriginalValue, New = p.CurrentValue };
                        break;
                    case AuditAction.Deleted:
                        changes[p.Metadata.Name] = new { Old = p.OriginalValue };
                        break;
                }
            }

            if (action == AuditAction.Updated && changes.Count == 0) continue;

            context.Set<AuditLog>().Add(AuditLog.Create(
                entry.Metadata.ClrType.Name, KeyOf(entry), action, _currentUser.UserId,
                JsonSerializer.Serialize(changes), _requestContext.CorrelationId));
        }
    }

    /// Khoá kép (UserRoles, RolePermissions…) nối bằng dấu phẩy theo thứ tự khai báo khoá.
    private static string KeyOf(EntityEntry entry)
        => string.Join(",", entry.Metadata.FindPrimaryKey()!.Properties
            .Select(p => entry.Property(p.Name).CurrentValue?.ToString()));
}
