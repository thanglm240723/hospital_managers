using QuanLyBenhVien.Application.Common.Interfaces;
using QuanLyBenhVien.Domain.Common;
using QuanLyBenhVien.Domain.Identity;
using QuanLyBenhVien.Infrastructure.DependencyInjection;
using QuanLyBenhVien.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace QuanLyBenhVien.UnitTests.Infrastructure.DependencyInjection;

file sealed class FakeCurrentUser : ICurrentUser
{
    public Guid? UserId => null;
    public Guid? SessionFamilyId => null;
    public bool IsAuthenticated => false;
}

file sealed class FakeRequestContext : IRequestContext
{
    public string? CorrelationId => null;
    public string? IpAddress => null;
    public string? UserAgent => null;
}

public class InfrastructureServiceExtensionsTests
{
    [Fact]
    public void AddInfrastructureServices_RegistersAllContracts()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=test;Username=test;Password=test",
                ["Jwt:Issuer"] = "test-issuer",
                ["Jwt:Audience"] = "test-audience",
                ["Jwt:SigningKey"] = new string('k', 32),
                ["Jwt:AccessTokenMinutes"] = "60"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddScoped<ICurrentUser, FakeCurrentUser>();
        services.AddScoped<IRequestContext, FakeRequestContext>();
        services.AddInfrastructureServices(configuration);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IUnitOfWork>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IUserRepository>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IPasswordHasher>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ITokenService>());
    }
}
