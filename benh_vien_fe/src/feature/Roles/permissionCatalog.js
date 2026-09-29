import { PERMISSIONS } from 'feature/Auth/permissionCodes';

// Danh mục quyền hiển thị theo nhóm module cho màn Vai trò & quyền — dữ liệu giả để dựng UI.
// Nhãn tiếng Việt + phân nhóm là DỰ_KIẾN, CẦN_XÁC_NHẬN khi BE có API permissions đầy đủ theo module.
export const PERMISSION_CATALOG = [
  {
    module: 'Quản trị tài khoản',
    items: [
      { code: PERMISSIONS.USERS_READ, label: 'Xem danh sách tài khoản' },
      { code: PERMISSIONS.USERS_CREATE, label: 'Tạo tài khoản' },
      { code: PERMISSIONS.USERS_ACTIVATE, label: 'Khóa/mở khóa tài khoản' },
      { code: PERMISSIONS.USERS_ROLES_MANAGE, label: 'Gán vai trò cho tài khoản' },
      { code: PERMISSIONS.USERS_PERMISSIONS_MANAGE, label: 'Cấp/thu hồi quyền lẻ' },
    ],
  },
  {
    module: 'Vai trò & quyền',
    items: [
      { code: PERMISSIONS.ROLES_READ, label: 'Xem vai trò' },
      { code: PERMISSIONS.ROLES_MANAGE, label: 'Quản lý vai trò tùy chỉnh' },
      { code: PERMISSIONS.PERMISSIONS_READ, label: 'Xem danh mục quyền' },
    ],
  },
  {
    module: 'Tiếp nhận (DỰ_KIẾN)',
    items: [
      { code: PERMISSIONS.PATIENTS_READ, label: 'Tìm kiếm hồ sơ bệnh nhân' },
      { code: PERMISSIONS.PATIENTS_CREATE, label: 'Tạo hồ sơ bệnh nhân' },
      { code: PERMISSIONS.RECEPTION_REGISTER, label: 'Tiếp nhận và cấp số khám' },
      { code: PERMISSIONS.QUEUES_CALL, label: 'Gọi số hàng chờ' },
    ],
  },
  {
    module: 'Lâm sàng (DỰ_KIẾN)',
    items: [
      { code: PERMISSIONS.VITALS_RECORD, label: 'Ghi nhận sinh hiệu' },
      { code: PERMISSIONS.ENCOUNTERS_EXAMINE, label: 'Khám và xác nhận bệnh án' },
    ],
  },
];

// 9 vai trò hệ thống theo mô tả nghiệp vụ — chỉ đọc, không sửa/xóa.
export const SYSTEM_ROLES = [
  { id: 'role-admin', name: 'Quản trị hệ thống', system: true },
  { id: 'role-reception', name: 'Lễ tân', system: true },
  { id: 'role-outpatient-nurse', name: 'Điều dưỡng ngoại trú', system: true },
  { id: 'role-doctor', name: 'Bác sĩ', system: true },
  { id: 'role-cashier', name: 'Thu ngân', system: true },
  { id: 'role-lab-tech', name: 'KTV CLS', system: true },
  { id: 'role-pharmacist', name: 'Dược sĩ', system: true },
  { id: 'role-inpatient-nurse', name: 'Điều dưỡng nội trú', system: true },
  { id: 'role-clinical-manager', name: 'Quản lý chuyên môn', system: true },
];
