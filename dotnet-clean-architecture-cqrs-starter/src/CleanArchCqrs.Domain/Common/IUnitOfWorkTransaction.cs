namespace CleanArchCqrs.Domain.Common;

/// Dispose mà chưa CommitAsync ⇒ rollback.
public interface IUnitOfWorkTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct = default);
}
