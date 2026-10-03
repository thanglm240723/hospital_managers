namespace QuanLyBenhVien.Persistence.Seed;

internal static class RoleDefaultsPlan
{
    public static IReadOnlyList<(string RoleCode, string PermissionCode)> PendingPairs(
        IReadOnlyDictionary<string, IReadOnlyList<string>> matrix,
        Func<string, bool> isActivated,
        IReadOnlySet<(string RoleCode, string PermissionCode)> alreadyApplied)
        => matrix
            .SelectMany(kv => kv.Value.Distinct(StringComparer.Ordinal).Select(code => (RoleCode: kv.Key, PermissionCode: code)))
            .Where(p => isActivated(p.PermissionCode) && !alreadyApplied.Contains(p))
            .OrderBy(p => p.RoleCode, StringComparer.Ordinal).ThenBy(p => p.PermissionCode, StringComparer.Ordinal)
            .ToList();
}
