namespace QuanLyBenhVien.Domain.Identity;

/// 9 vai trò theo Đặc tả nghiệp vụ v2.0 — seed với IsSystem = true.
public static class SystemRoles
{
    public const string Receptionist = "receptionist";
    public const string OutpatientNurse = "outpatient-nurse";
    public const string Doctor = "doctor";
    public const string InpatientNurse = "inpatient-nurse";
    public const string LabTechnician = "lab-technician";
    public const string Pharmacist = "pharmacist";
    public const string Cashier = "cashier";
    public const string ClinicalManager = "clinical-manager";
    public const string Admin = "admin";

    public static IReadOnlyList<(string Code, string Name)> All { get; } =
    [
        (Receptionist, "Lễ tân"),
        (OutpatientNurse, "Điều dưỡng ngoại trú"),
        (Doctor, "Bác sĩ"),
        (InpatientNurse, "Điều dưỡng nội trú"),
        (LabTechnician, "KTV CLS"),
        (Pharmacist, "Dược sĩ"),
        (Cashier, "Thu ngân"),
        (ClinicalManager, "Quản lý chuyên môn"),
        (Admin, "Admin"),
    ];
}
