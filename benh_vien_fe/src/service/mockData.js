// Dữ liệu giả dùng chung cho các client mock — tên giả, số che, không dữ liệu thật.
export const FAKE_NAMES = [
  'Nguyễn Văn An', 'Trần Thị Bình', 'Lê Hoàng Cường', 'Phạm Thị Dung', 'Hoàng Văn Em',
  'Vũ Thị Phương', 'Đặng Văn Giang', 'Bùi Thị Hoa', 'Đỗ Văn Inh', 'Ngô Thị Kim',
];

export const maskDocumentNumber = value => (value ? `****${String(value).slice(-4)}` : '****');
export const maskPhoneNumber = value => (value ? `****${String(value).slice(-4)}` : '****');

let counter = 1000;
export const nextMockId = (prefix) => {
  counter += 1;
  return `${prefix}-${counter}`;
};
