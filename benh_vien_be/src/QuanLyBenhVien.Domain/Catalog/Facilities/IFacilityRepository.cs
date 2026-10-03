namespace QuanLyBenhVien.Domain.Catalog.Facilities;

public interface IFacilityRepository
{
    Task<Branch?> GetBranchAsync(Guid id, CancellationToken ct = default);
    Task<Department?> GetDepartmentAsync(Guid id, CancellationToken ct = default);
    Task<Room?> GetRoomAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Department>> GetDepartmentsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);
    /// Khóa hàng (FOR UPDATE), cần transaction đang mở.
    Task<Branch?> GetBranchForUpdateAsync(Guid id, CancellationToken ct = default);
    Task<Department?> GetDepartmentForUpdateAsync(Guid id, CancellationToken ct = default);
    Task<Room?> GetRoomForUpdateAsync(Guid id, CancellationToken ct = default);
    Task<bool> HasActiveDepartmentsAsync(Guid branchId, CancellationToken ct = default);
    Task<bool> HasActiveRoomsAsync(Guid departmentId, CancellationToken ct = default);
    Task AddAsync(Branch branch, CancellationToken ct = default);
    Task AddAsync(Department department, CancellationToken ct = default);
    Task AddAsync(Room room, CancellationToken ct = default);
}
