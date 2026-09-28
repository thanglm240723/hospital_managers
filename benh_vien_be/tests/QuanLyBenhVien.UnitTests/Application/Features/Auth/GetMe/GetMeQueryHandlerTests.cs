using QuanLyBenhVien.Application.Common.Identity;
using QuanLyBenhVien.Application.Features.Auth.Common;
using QuanLyBenhVien.Application.Features.Auth.GetMe;
using QuanLyBenhVien.Application.Features.Auth.ValidateSession;
using Xunit;

namespace QuanLyBenhVien.UnitTests.Application.Features.Auth.GetMe;

file sealed class FakeCurrentUser : ICurrentUser
{
    public Guid? UserId { get; init; }
    public Guid? SessionFamilyId => null;
}

file sealed class FakeAuthReadService(MeDto? me) : IAuthReadService
{
    public Task<SessionStateDto?> GetSessionStateAsync(Guid sessionFamilyId, CancellationToken ct = default)
        => Task.FromResult<SessionStateDto?>(null);

    public Task<MeDto?> GetMeAsync(Guid userId, CancellationToken ct = default) => Task.FromResult(me);
}

public class GetMeQueryHandlerTests
{
    [Fact]
    public async Task ExistingUser_ReturnsMeDto()
    {
        var dto = new MeDto(Guid.NewGuid(), "a@b.vn", "A", null, [], ["users.read"], false);
        var handler = new GetMeQueryHandler(new FakeCurrentUser { UserId = dto.Id }, new FakeAuthReadService(dto));

        var result = await handler.Handle(new GetMeQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(dto, result.Value);
    }

    [Fact]
    public async Task ReadServiceReturnsNull_ReturnsUnauthenticated()
    {
        var handler = new GetMeQueryHandler(new FakeCurrentUser { UserId = Guid.NewGuid() }, new FakeAuthReadService(null));

        var result = await handler.Handle(new GetMeQuery(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AuthErrors.Unauthenticated, result.Error);
    }

    [Fact]
    public async Task NoCurrentUser_ReturnsUnauthenticated()
    {
        var handler = new GetMeQueryHandler(new FakeCurrentUser { UserId = null }, new FakeAuthReadService(null));

        var result = await handler.Handle(new GetMeQuery(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AuthErrors.Unauthenticated, result.Error);
    }
}
