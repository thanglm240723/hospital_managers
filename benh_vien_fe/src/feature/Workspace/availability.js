// Trạng thái phát hành tính năng theo khóa availability của route registry — KHÔNG thay quyền.
// Mốc plan 04: chưa màn nào có endpoint thật đã nghiệm thu ⇒ tắt hết, màn có quyền hiện "khu vực đang hoàn thiện".
// Plan 05/06 bật từng màn quản trị sau khi nghiệm thu API; khu vực lâm sàng chỉ bật khi module có quyền/endpoint thật.
export const FEATURE_AVAILABILITY = {
  'admin.users': false,
  'admin.roles': true, // plan 05: API vai trò/quyền đã triển khai
  'reception.patients': false,
  'reception.intake': false,
  'clinic.queue': false,
  'clinic.encounter': false,
  'vitals.queue': false,
};

export default FEATURE_AVAILABILITY;
