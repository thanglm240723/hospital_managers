using System.Text.RegularExpressions;
using QuanLyBenhVien.Domain.Common;
using QuanLyBenhVien.Domain.Common.Auditing;

namespace QuanLyBenhVien.Domain.Identity.Staff;

public sealed class StaffProfile : AggregateRoot<Guid>, IAuditable
{
    private static readonly Regex CodePattern = new("^[A-Z0-9][A-Z0-9-]{1,29}\\z", RegexOptions.Compiled);

    private readonly List<StaffWorkScope> _workScopes = new();

    public Guid UserId { get; private set; }
    public string StaffCode { get; private set; } = default!;
    public bool IsActive { get; private set; }
    public uint RowVersion { get; private set; }
    public IReadOnlyCollection<StaffWorkScope> WorkScopes => _workScopes.AsReadOnly();

    private StaffProfile() { }

    public static StaffProfile Create(Guid userId, string staffCode)
        => new() { Id = Guid.CreateVersion7(), UserId = userId, StaffCode = ValidCode(staffCode), IsActive = true };

    public void ChangeStaffCode(string staffCode) => StaffCode = ValidCode(staffCode);

    public void SetActive(bool isActive) => IsActive = isActive;

    /// departments: (DepartmentId, BranchId) đã được Application kiểm tồn tại + đang dùng. Trả true nếu tập thay đổi.
    public bool SetWorkScopes(IEnumerable<(Guid DepartmentId, Guid BranchId)> departments)
    {
        var target = departments.GroupBy(d => d.DepartmentId).Select(g => g.First()).ToList();
        var removed = _workScopes.RemoveAll(s => !target.Any(t => t.DepartmentId == s.DepartmentId && t.BranchId == s.BranchId));
        var toAdd = target.Where(t => _workScopes.All(s => s.DepartmentId != t.DepartmentId)).ToList();
        foreach (var t in toAdd)
            _workScopes.Add(new StaffWorkScope(Id, t.DepartmentId, t.BranchId));
        return removed > 0 || toAdd.Count > 0;
    }

    private static string ValidCode(string code)
    {
        if (string.IsNullOrEmpty(code) || !CodePattern.IsMatch(code))
            throw new ArgumentException("Staff code must be 2-30 chars of A-Z, 0-9, '-' and start with a letter or digit.", nameof(code));
        return code;
    }
}
