using Microsoft.Extensions.DependencyInjection;
using QuanLyBenhVien.Application.Common.Identity;
using QuanLyBenhVien.Domain.Catalog.Facilities;
using QuanLyBenhVien.Domain.Identity.Staff;
using QuanLyBenhVien.IntegrationTests.Helpers;
using QuanLyBenhVien.IntegrationTests.Infrastructure;
using QuanLyBenhVien.Persistence;
using QuanLyBenhVien.Persistence.Authorization;
using Xunit;

namespace QuanLyBenhVien.IntegrationTests.Authorization;

[Collection(IntegrationCollection.Name)]
public class AccessContextTests
{
    private readonly ContainersFixture _containers;
    public AccessContextTests(ContainersFixture containers) => _containers = containers;

    private sealed class FakeUser(Guid id) : ICurrentUser
    {
        public Guid? UserId => id;
        public Guid? SessionFamilyId => null;
        public int? SecurityVersion => null;
    }

    private static string Code() => "T" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();

    private static async Task<(Guid userId, Guid activeDept, Guid activeBranch, Guid inactiveDept)> SeedAsync(ApiFactory factory, bool profileActive)
    {
        var userId = await TestData.CreateUserAsync(factory, TestData.NewEmail());
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var b1 = Branch.Create(Code(), "CS 1");
        var b2 = Branch.Create(Code(), "CS 2");
        var d1 = Department.Create(b1.Id, Code(), "Khoa 1", DepartmentKind.Clinical);
        var d2 = Department.Create(b2.Id, Code(), "Khoa 2", DepartmentKind.Clinical);
        d2.SetActive(false);
        var profile = StaffProfile.Create(userId, Code());
        profile.SetWorkScopes([(d1.Id, b1.Id), (d2.Id, b2.Id)]);
        if (!profileActive) profile.SetActive(false);
        db.AddRange(b1, b2, d1, d2, profile);
        await db.SaveChangesAsync();
        return (userId, d1.Id, b1.Id, d2.Id);
    }

    private static AccessContext Create(Guid userId, AsyncServiceScope scope)
        => new(scope.ServiceProvider.GetRequiredService<AppDbContext>(), new FakeUser(userId));

    [Fact]
    public async Task OnlyActiveDepartments_AreInScope_AndResultIsCached()
    {
        await using var factory = await ApiFactory.CreateAsync(_containers);
        var (userId, d1, b1, d2) = await SeedAsync(factory, profileActive: true);
        await using var scope = factory.Services.CreateAsyncScope();
        var ctx = Create(userId, scope);

        var scope1 = await ctx.GetAsync(default);

        Assert.True(scope1.HasWorkScope);
        Assert.Equal([d1], scope1.DepartmentIds);
        Assert.Equal([b1], scope1.BranchIds);
        Assert.DoesNotContain(d2, scope1.DepartmentIds);
        Assert.Same(scope1, await ctx.GetAsync(default));
    }

    [Fact]
    public async Task InactiveProfile_HasNoWorkScope()
    {
        await using var factory = await ApiFactory.CreateAsync(_containers);
        var (userId, _, _, _) = await SeedAsync(factory, profileActive: false);
        await using var scope = factory.Services.CreateAsyncScope();

        var result = await Create(userId, scope).GetAsync(default);

        Assert.Null(result.StaffProfileId);
        Assert.False(result.HasWorkScope);
    }
}
