using QuanLyBenhVien.Application.Common.Caching;
using QuanLyBenhVien.Application.Features.Auth.Common;
using QuanLyBenhVien.Application.Features.Auth.GetMe;
using QuanLyBenhVien.Application.Features.Auth.ValidateSession;
using QuanLyBenhVien.Domain.Identity.Sessions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace QuanLyBenhVien.UnitTests.Application.Features.Auth.ValidateSession;

file sealed class FakeAuthReadService : IAuthReadService
{
    public SessionStateDto? State { get; set; }
    public List<Guid> RequestedFamilyIds { get; } = [];

    public Task<SessionStateDto?> GetSessionStateAsync(Guid sessionFamilyId, CancellationToken ct = default)
    {
        RequestedFamilyIds.Add(sessionFamilyId);
        return Task.FromResult(State);
    }

    public Task<MeDto?> GetMeAsync(Guid userId, CancellationToken ct = default) => Task.FromResult<MeDto?>(null);
}

file sealed class FakeSessionCache : ISessionCache
{
    public CacheGeneration? GenerationToReturn { get; set; }
    public List<string> CallOrder { get; } = [];
    public SessionCacheEntry? WrittenEntry { get; private set; }
    public CacheGeneration? WrittenExpected { get; private set; }

    public Task<CacheGeneration?> ReadGenerationAsync(Guid sessionFamilyId, CancellationToken ct = default)
    {
        CallOrder.Add("read-generation");
        return Task.FromResult(GenerationToReturn);
    }

    public Task SetIfGenerationUnchangedAsync(SessionCacheEntry entry, CacheGeneration expected, CancellationToken ct = default)
    {
        CallOrder.Add("write");
        WrittenEntry = entry;
        WrittenExpected = expected;
        return Task.CompletedTask;
    }
}

public class ValidateSessionQueryHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 8, 0, 0, TimeSpan.Zero);

    private static SessionStateDto ActiveState(int sv = 3) => new(
        UserId: Guid.NewGuid(), Status: SessionStatus.Active, AbsoluteExpiresAtUtc: Now.AddDays(1), IsActive: true, SecurityVersion: sv);

    [Fact]
    public async Task Valid_WithGeneration_ReturnsValidAndWritesCacheWithGeneration()
    {
        var authRead = new FakeAuthReadService { State = ActiveState() };
        var cache = new FakeSessionCache { GenerationToReturn = new CacheGeneration("gen-1") };
        var handler = new ValidateSessionQueryHandler(authRead, cache, new FakeTimeProvider(Now));
        var familyId = Guid.NewGuid();

        var result = await handler.Handle(new ValidateSessionQuery(familyId, 3), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Valid);
        Assert.NotNull(cache.WrittenEntry);
        Assert.Equal(new CacheGeneration("gen-1"), cache.WrittenExpected);
        Assert.Equal(new[] { "read-generation", "write" }, cache.CallOrder);
    }

    [Theory]
    [InlineData(SessionStatus.Revoked, true, 3)]
    public async Task RevokedFamily_IsInvalid_DoesNotWriteCache(SessionStatus status, bool isActive, int sv)
    {
        var authRead = new FakeAuthReadService { State = new SessionStateDto(Guid.NewGuid(), status, Now.AddDays(1), isActive, sv) };
        var cache = new FakeSessionCache { GenerationToReturn = new CacheGeneration("gen-1") };
        var handler = new ValidateSessionQueryHandler(authRead, cache, new FakeTimeProvider(Now));

        var result = await handler.Handle(new ValidateSessionQuery(Guid.NewGuid(), sv), CancellationToken.None);

        Assert.False(result.Value.Valid);
        Assert.Null(cache.WrittenEntry);
    }

    [Fact]
    public async Task StaleSecurityVersion_IsInvalid()
    {
        var authRead = new FakeAuthReadService { State = ActiveState(sv: 5) };
        var cache = new FakeSessionCache();
        var handler = new ValidateSessionQueryHandler(authRead, cache, new FakeTimeProvider(Now));

        var result = await handler.Handle(new ValidateSessionQuery(Guid.NewGuid(), 4), CancellationToken.None);

        Assert.False(result.Value.Valid);
        Assert.Null(cache.WrittenEntry);
    }

    [Fact]
    public async Task Expired_IsInvalid()
    {
        var authRead = new FakeAuthReadService
        {
            State = new SessionStateDto(Guid.NewGuid(), SessionStatus.Active, Now.AddDays(-1), true, 1)
        };
        var handler = new ValidateSessionQueryHandler(authRead, new FakeSessionCache(), new FakeTimeProvider(Now));

        var result = await handler.Handle(new ValidateSessionQuery(Guid.NewGuid(), 1), CancellationToken.None);

        Assert.False(result.Value.Valid);
    }

    [Fact]
    public async Task DeactivatedUser_IsInvalid()
    {
        var authRead = new FakeAuthReadService
        {
            State = new SessionStateDto(Guid.NewGuid(), SessionStatus.Active, Now.AddDays(1), false, 1)
        };
        var handler = new ValidateSessionQueryHandler(authRead, new FakeSessionCache(), new FakeTimeProvider(Now));

        var result = await handler.Handle(new ValidateSessionQuery(Guid.NewGuid(), 1), CancellationToken.None);

        Assert.False(result.Value.Valid);
    }

    [Fact]
    public async Task UnknownFamily_IsInvalid()
    {
        var authRead = new FakeAuthReadService { State = null };
        var handler = new ValidateSessionQueryHandler(authRead, new FakeSessionCache(), new FakeTimeProvider(Now));

        var result = await handler.Handle(new ValidateSessionQuery(Guid.NewGuid(), 1), CancellationToken.None);

        Assert.False(result.Value.Valid);
    }

    [Fact]
    public async Task RedisError_GenerationNull_StillValid_ButDoesNotWriteCache()
    {
        var authRead = new FakeAuthReadService { State = ActiveState() };
        var cache = new FakeSessionCache { GenerationToReturn = null };
        var handler = new ValidateSessionQueryHandler(authRead, cache, new FakeTimeProvider(Now));

        var result = await handler.Handle(new ValidateSessionQuery(Guid.NewGuid(), 3), CancellationToken.None);

        Assert.True(result.Value.Valid);
        Assert.Null(cache.WrittenEntry);
    }

    [Fact]
    public async Task GenerationIsReadBeforeReadService()
    {
        var order = new List<string>();
        var authRead = new RecordingAuthReadService(order, ActiveState());
        var cache = new RecordingSessionCache(order);

        var handler = new ValidateSessionQueryHandler(authRead, cache, new FakeTimeProvider(Now));
        await handler.Handle(new ValidateSessionQuery(Guid.NewGuid(), 3), CancellationToken.None);

        Assert.Equal("cache-read", order[0]);
        Assert.Equal("db-read", order[1]);
    }

    private sealed class RecordingAuthReadService(List<string> order, SessionStateDto state) : IAuthReadService
    {
        public Task<SessionStateDto?> GetSessionStateAsync(Guid sessionFamilyId, CancellationToken ct = default)
        {
            order.Add("db-read");
            return Task.FromResult<SessionStateDto?>(state);
        }

        public Task<MeDto?> GetMeAsync(Guid userId, CancellationToken ct = default) => Task.FromResult<MeDto?>(null);
    }

    private sealed class RecordingSessionCache(List<string> order) : ISessionCache
    {
        public Task<CacheGeneration?> ReadGenerationAsync(Guid sessionFamilyId, CancellationToken ct = default)
        {
            order.Add("cache-read");
            return Task.FromResult<CacheGeneration?>(new CacheGeneration("g"));
        }

        public Task SetIfGenerationUnchangedAsync(SessionCacheEntry entry, CacheGeneration expected, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
