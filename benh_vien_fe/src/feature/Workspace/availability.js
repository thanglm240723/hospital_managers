// Trạng thái phát hành tính năng theo khóa availability của route registry — KHÔNG thay quyền.
// Mốc plan 04: chưa màn nào có endpoint thật đã nghiệm thu ⇒ tắt hết, màn có quyền hiện "khu vực đang hoàn thiện".
// Plan 05/06 đã bật màn quản trị; khu vực lâm sàng chỉ bật khi module có quyền/endpoint thật.
import { isMockApiEnabled } from 'service/mockMode';

export const FEATURE_AVAILABILITY = {
  'admin.users': !isMockApiEnabled(), // plan 06: API thật; màn Tài khoản không có mock nên tắt khi bật mock
  'admin.roles': true, // plan 05: API vai trò/quyền đã triển khai
  'admin.facilities': !isMockApiEnabled(), // plan nền lớp 2: API thật; không có mock nên tắt khi bật mock
  'reception.patients': false,
  'reception.intake': false,
  'clinic.queue': false,
  'clinic.encounter': false,
  'vitals.queue': false,
};

export default FEATURE_AVAILABILITY;
