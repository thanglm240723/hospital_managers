using QuanLyBenhVien.Application.Common.Behaviors;
using QuanLyBenhVien.Application.Common.Interfaces;
using QuanLyBenhVien.Domain.Common;
using QuanLyBenhVien.Domain.Common.Auditing;
using Xunit;

namespace QuanLyBenhVien.UnitTests.Application.Behaviors;

sealed record SensitiveQuery(string Id) : IAuditedRequest
{
    public string AuditAction => "records.read";
    public string AuditResourceType => "MedicalRecord";
    public string? AuditResourceId => Id;
}

sealed record PlainQuery;

sealed class RecordingAuditRecorder : IAuditRecorder
{
    public List<(string Action, AuditResult Result, string? ResourceId)> Records { get; } = new();

    public void Record(string action, AuditResult result, string? reason = null, string? resourceType = null,
        string? resourceId = null, Guid? actorId = null, IReadOnlyDictionary<string, object?>? metadata = null)
        => Records.Add((action, result, resourceId));
}

sealed class StubUnitOfWork : IUnitOfWork
{
    public bool Fail { get; init; }
    public int Saves { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        if (Fail) throw new InvalidOperationException("audit store down");
        Saves++;
        return Task.FromResult(1);
    }

    public Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken ct = default) => throw new NotSupportedException();
}

public class AuditBehaviorTests
{
    [Fact]
    public async Task AuditedRequest_IsRecordedAndSavedBeforeDataIsReturned()
    {
        var recorder = new RecordingAuditRecorder();
        var unitOfWork = new StubUnitOfWork();
        var behavior = new AuditBehavior<SensitiveQuery, string>(recorder, unitOfWork);

        var result = await behavior.Handle(new SensitiveQuery("r-1"), _ => Task.FromResult("secret"), CancellationToken.None);

        Assert.Equal("secret", result);
        Assert.Equal(("records.read", AuditResult.Succeeded, "r-1"), Assert.Single(recorder.Records));
        Assert.Equal(1, unitOfWork.Saves);
    }

    [Fact]
    public async Task AuditStoreFailure_WithholdsTheData()
        => await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new AuditBehavior<SensitiveQuery, string>(new RecordingAuditRecorder(), new StubUnitOfWork { Fail = true })
                .Handle(new SensitiveQuery("r-1"), _ => Task.FromResult("secret"), CancellationToken.None));

    [Fact]
    public async Task PlainRequest_IsNotAudited()
    {
        var recorder = new RecordingAuditRecorder();

        await new AuditBehavior<PlainQuery, int>(recorder, new StubUnitOfWork())
            .Handle(new PlainQuery(), _ => Task.FromResult(1), CancellationToken.None);

        Assert.Empty(recorder.Records);
    }
}
