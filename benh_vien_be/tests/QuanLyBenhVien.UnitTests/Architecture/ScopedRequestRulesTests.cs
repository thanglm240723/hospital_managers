using QuanLyBenhVien.Application.Common.Authorization;
using QuanLyBenhVien.UnitTests.Architecture.Fakes.Features.Clinical.X;
using QuanLyBenhVien.UnitTests.Architecture.Fakes.Other;
using Xunit;

namespace QuanLyBenhVien.UnitTests.Architecture;

public sealed class ScopedRequestRulesTests
{
    private static readonly IReadOnlyDictionary<Type, string> None = new Dictionary<Type, string>();

    [Fact]
    public void MissingMarker_InGovernedFeature_IsViolation() =>
        Assert.Single(ScopedRequestRules.Violations([typeof(NoMarker)], None));

    [Fact]
    public void BothMarkers_IsViolation() =>
        Assert.Single(ScopedRequestRules.Violations([typeof(BothMarkers)], None));

    [Fact]
    public void UnscopedNotApproved_IsViolation() =>
        Assert.Single(ScopedRequestRules.Violations([typeof(Unscoped)], None));

    [Fact]
    public void UnscopedApproved_IsOk() =>
        Assert.Empty(ScopedRequestRules.Violations([typeof(Unscoped)], new Dictionary<Type, string> { [typeof(Unscoped)] = "lý do" }));

    [Fact]
    public void ApprovedNotImplementingUnscoped_IsViolation() =>
        Assert.Single(ScopedRequestRules.Violations([typeof(Scoped)], new Dictionary<Type, string> { [typeof(Scoped)] = "lý do" }));

    [Fact]
    public void ApprovedNotScannedRequest_IsViolation()
    {
        var approved = new Dictionary<Type, string>
        {
            [typeof(AbstractUnscoped)] = "lý do",
            [typeof(NotARequest)] = "lý do",
            [typeof(Unscoped)] = "lý do", // request thật nhưng không nằm trong tập quét
        };
        Assert.Equal(3, ScopedRequestRules.Violations([], approved).Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ApprovedBlankReason_IsViolation(string reason) =>
        Assert.Single(ScopedRequestRules.Violations([typeof(Unscoped)], new Dictionary<Type, string> { [typeof(Unscoped)] = reason }));

    [Fact]
    public void ScopedInGovernedFeature_IsOk() =>
        Assert.Empty(ScopedRequestRules.Violations([typeof(Scoped)], None));

    [Fact]
    public void NoMarker_OutsideGovernedFeature_IsOk() =>
        Assert.Empty(ScopedRequestRules.Violations([typeof(Ungoverned)], None));

    [Fact]
    public void ApplicationRequests_FollowScopedRequestRules()
    {
        var types = typeof(IScopedRequest).Assembly.GetTypes();
        Assert.Empty(ScopedRequestRules.Violations(types, ApprovedUnscopedRequests.All));
    }
}
