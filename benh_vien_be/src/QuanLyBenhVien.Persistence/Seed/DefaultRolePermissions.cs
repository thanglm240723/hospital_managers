using QuanLyBenhVien.Domain.Identity;

namespace QuanLyBenhVien.Persistence.Seed;

/// Ma trận vai trò × quyền mặc định — spec phân quyền hai lớp §5. Khóa theo chuỗi mã để chứa cả mã CHƯA kích hoạt;
/// seeder chỉ áp mã đã có trong Permissions.All. Sửa ma trận = sửa spec.
internal static class DefaultRolePermissions
{
    public static IReadOnlyList<string> PlannedCodes { get; } =
    [
        "users.read", "users.create", "users.activate", "users.roles.manage", "users.permissions.manage",
        "roles.read", "roles.manage", "permissions.read",
        "facilities.read", "facilities.manage", "staff-profiles.read", "staff-profiles.manage",
        "catalog.read", "catalog.manage", "settings.manage",
        "patients.read", "patients.create", "patients.update",
        "encounters.register", "clinic-sessions.read", "clinic-sessions.manage", "clinic-sessions.staff.assign",
        "queue.read", "queue.call", "queue.transfer", "queue.display",
        "vitals.read", "vitals.record", "encounters.read", "encounters.examine", "encounters.confirm", "encounters.amend",
        "patient-history.read", "orders.read", "orders.create", "orders.cancel",
        "results.read", "results.record", "results.confirm", "prescriptions.read", "prescriptions.create",
        "admission-requests.create", "dispensing.read", "dispensing.record",
        "inpatient.admit", "inpatient.read", "care-team.assign", "beds.read", "beds.assign", "beds.clean", "beds.manage",
        "bed-waitlist.manage", "medical-orders.create", "medical-orders.execute", "inpatient.discharge",
        "billing.read", "billing.collect", "billing.deposit", "billing.refund", "billing.adjust", "billing.settle",
        "billing.reopen", "billing.insurance-rate.override", "billing.emergency-exception.approve",
        "documents.read", "documents.upload", "documents.export",
        "access-grants.request-emergency", "access-grants.approve", "audit.read", "reports.read",
    ];

    private static readonly string[] Common = ["catalog.read", "facilities.read"];

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ByRole { get; } = new Dictionary<string, IReadOnlyList<string>>
    {
        [SystemRoles.Receptionist] = [.. Common, "patients.read", "patients.create", "patients.update", "encounters.register",
            "clinic-sessions.read", "queue.read", "queue.transfer", "inpatient.admit"],
        [SystemRoles.OutpatientNurse] = [.. Common, "patients.read", "clinic-sessions.read", "queue.read", "queue.call",
            "vitals.read", "vitals.record"],
        [SystemRoles.Doctor] = [.. Common, "patients.read", "clinic-sessions.read", "queue.read", "queue.call", "vitals.read",
            "encounters.read", "encounters.examine", "encounters.confirm", "encounters.amend", "patient-history.read",
            "orders.read", "orders.create", "results.read", "prescriptions.read", "prescriptions.create",
            "admission-requests.create", "inpatient.read", "medical-orders.create", "inpatient.discharge",
            "documents.read", "documents.upload", "documents.export"],
        [SystemRoles.InpatientNurse] = [.. Common, "patients.read", "inpatient.read", "beds.read", "beds.assign", "beds.clean",
            "bed-waitlist.manage", "medical-orders.execute", "vitals.read", "vitals.record", "orders.read", "results.read",
            "prescriptions.read", "documents.read", "documents.upload"],
        [SystemRoles.LabTechnician] = [.. Common, "patients.read", "orders.read", "results.read", "results.record",
            "documents.read", "documents.upload"],
        [SystemRoles.Pharmacist] = [.. Common, "patients.read", "prescriptions.read", "dispensing.read", "dispensing.record"],
        [SystemRoles.Cashier] = [.. Common, "patients.read", "billing.read", "billing.collect", "billing.deposit",
            "billing.refund", "billing.settle"],
        [SystemRoles.ClinicalManager] = [.. Common, "patients.read", "clinic-sessions.read", "clinic-sessions.manage",
            "clinic-sessions.staff.assign", "queue.read", "queue.transfer", "care-team.assign", "access-grants.approve",
            "beds.read", "beds.manage", "bed-waitlist.manage", "staff-profiles.read", "audit.read", "reports.read"],
        [SystemRoles.Admin] = [.. Permissions.IdentityAccess.Select(p => p.Code), "facilities.read", "facilities.manage",
            "staff-profiles.read", "staff-profiles.manage", "catalog.read", "catalog.manage", "settings.manage", "audit.read"],
    };

    /// Không gán mặc định; cấp lẻ hoặc vai trò tùy biến (spec §5 cột "Bổ sung").
    public static IReadOnlySet<string> Supplementary { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "billing.deposit", "orders.cancel", "results.confirm", "billing.adjust", "billing.reopen",
        "billing.insurance-rate.override", "billing.emergency-exception.approve", "queue.display",
        "access-grants.request-emergency",
    };
}
