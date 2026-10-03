using QuanLyBenhVien.Application.Common.Authorization;
using QuanLyBenhVien.Application.Common.Messaging;

namespace QuanLyBenhVien.UnitTests.Architecture;

internal static class ScopedRequestRules
{
    /// Feature cần marker: các module có lớp 2 (spec §7.4) + module nền của plan này.
    public static readonly string[] GovernedFeatures =
        ["ReceptionQueue", "Clinical", "Inpatient", "Billing", "Documents", "Pharmacy", "Facilities", "StaffProfiles"];

    public static IReadOnlyList<string> Violations(IEnumerable<Type> types, IReadOnlyDictionary<Type, string> approved)
    {
        var violations = new List<string>();
        var scanned = types.Where(IsRequest).ToHashSet();
        foreach (var t in scanned)
        {
            var scoped = typeof(IScopedRequest).IsAssignableFrom(t);
            var unscoped = typeof(IUnscopedRequest).IsAssignableFrom(t);
            if (scoped && unscoped) violations.Add($"{t.FullName}: implement cả hai marker");
            else if (!scoped && !unscoped && IsGoverned(t)) violations.Add($"{t.FullName}: thiếu marker");
            else if (unscoped && !approved.ContainsKey(t)) violations.Add($"{t.FullName}: IUnscopedRequest chưa được duyệt");
        }
        foreach (var (t, reason) in approved)
        {
            if (!scanned.Contains(t)) violations.Add($"{t.FullName}: mục thừa, không phải request được quét");
            else if (!typeof(IUnscopedRequest).IsAssignableFrom(t)) violations.Add($"{t.FullName}: có trong ApprovedUnscopedRequests nhưng không implement IUnscopedRequest");
            else if (string.IsNullOrWhiteSpace(reason)) violations.Add($"{t.FullName}: thiếu lý do");
        }
        return violations;
    }

    private static bool IsRequest(Type t) => t is { IsClass: true, IsAbstract: false } && t.GetInterfaces().Any(i =>
        i.IsGenericType && (i.GetGenericTypeDefinition() == typeof(ICommand<>) || i.GetGenericTypeDefinition() == typeof(IQuery<>)));

    private static bool IsGoverned(Type t) => GovernedFeatures.Any(f => t.Namespace?.Contains($".Features.{f}.", StringComparison.Ordinal) == true);
}
