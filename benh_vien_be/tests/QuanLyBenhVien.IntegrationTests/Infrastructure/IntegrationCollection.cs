using Xunit;

namespace QuanLyBenhVien.IntegrationTests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class IntegrationCollection : ICollectionFixture<ContainersFixture>
{
    public const string Name = "integration";
}
