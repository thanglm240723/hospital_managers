// Khu vực làm việc = nhóm màn hình theo nhiệm vụ. Khu vực nào hiện ra và route mặc định được tính từ quyền
// trong route registry (routeAccess.js) — không dựa trên tên role.
export const WORKSPACES = [
  { id: 'admin', name: 'Quản trị hệ thống', description: 'Quản lý tài khoản, vai trò và phân quyền, cơ cấu tổ chức, nhân sự.' },
  { id: 'reception', name: 'Tiếp nhận', description: 'Tìm hồ sơ bệnh nhân, tiếp nhận và cấp số khám.' },
  { id: 'clinic', name: 'Khám ngoại trú', description: 'Hàng chờ khám, phiếu khám, chỉ định và đơn thuốc.' },
  { id: 'vitals', name: 'Sinh hiệu', description: 'Đo và ghi nhận sinh hiệu trước khi khám.' },
];

export const findWorkspace = id => WORKSPACES.find(w => w.id === id) || null;
