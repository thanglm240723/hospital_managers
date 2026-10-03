using System.Text.RegularExpressions;

namespace QuanLyBenhVien.Domain.Catalog.Facilities;

internal static class FacilityRules
{
    private static readonly Regex CodePattern = new("^[A-Z0-9][A-Z0-9-]{0,29}\\z", RegexOptions.Compiled);

    public static string ValidCode(string code)
    {
        if (string.IsNullOrEmpty(code) || !CodePattern.IsMatch(code))
            throw new ArgumentException("Code must be 1-30 chars of A-Z, 0-9, '-' and start with a letter or digit.", nameof(code));
        return code;
    }

    public static string ValidName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name cannot be empty.", nameof(name));
        var trimmed = name.Trim();
        if (trimmed.Length > 200)
            throw new ArgumentException("Name cannot exceed 200 characters.", nameof(name));
        return trimmed;
    }
}
