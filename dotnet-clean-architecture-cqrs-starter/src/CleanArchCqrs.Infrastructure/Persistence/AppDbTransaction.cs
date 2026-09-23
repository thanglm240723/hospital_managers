using CleanArchCqrs.Domain.Common;
using Microsoft.EntityFrameworkCore.Storage;

namespace CleanArchCqrs.Infrastructure.Persistence;

internal sealed class AppDbTransaction : IUnitOfWorkTransaction
{
    private readonly IDbContextTransaction _inner;

    public AppDbTransaction(IDbContextTransaction inner) => _inner = inner;

    public Task CommitAsync(CancellationToken ct = default) => _inner.CommitAsync(ct);

    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}
