using System.Text.Json;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Common.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CleanArchCqrs.Infrastructure.Persistence.Interceptors;

public sealed class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUser _currentUser;

    public AuditSaveChangesInterceptor(ICurrentUser currentUser) => _currentUser = currentUser;

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
            var entityName = entry.Entity.GetType().Name;
            var entityId = entry.Property("Id").CurrentValue?.ToString() ?? "";
            var changes = new Dictionary<string, object?>();

            switch (entry.State)
            {
                case EntityState.Added:
                    foreach (var p in entry.Properties.Where(p => p.Metadata.Name != "PasswordHash"))
                        changes[p.Metadata.Name] = new { New = p.CurrentValue };
                    context.Set<AuditLog>().Add(AuditLog.Create(
                        entityName, entityId, AuditAction.Created, _currentUser.UserId, JsonSerializer.Serialize(changes)));
                    break;

                case EntityState.Modified:
                    foreach (var p in entry.Properties.Where(p => p.IsModified && p.Metadata.Name != "PasswordHash"))
                        changes[p.Metadata.Name] = new { Old = p.OriginalValue, New = p.CurrentValue };
                    if (changes.Count > 0)
                        context.Set<AuditLog>().Add(AuditLog.Create(
                            entityName, entityId, AuditAction.Updated, _currentUser.UserId, JsonSerializer.Serialize(changes)));
                    break;

                case EntityState.Deleted:
                    foreach (var p in entry.Properties.Where(p => p.Metadata.Name != "PasswordHash"))
                        changes[p.Metadata.Name] = new { Old = p.OriginalValue };
                    context.Set<AuditLog>().Add(AuditLog.Create(
                        entityName, entityId, AuditAction.Deleted, _currentUser.UserId, JsonSerializer.Serialize(changes)));
                    break;
            }
        }
    }
}
