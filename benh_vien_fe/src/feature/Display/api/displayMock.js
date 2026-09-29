// Bảng gọi số TV chỉ hiển thị số + phòng — không tên, không dữ liệu định danh bệnh nhân.
const ROOMS = ['Phòng 01', 'Phòng 02', 'Phòng 03', 'Phòng 04'];

let seed = 40;

export function nextSnapshot() {
  seed += 1;
  return ROOMS.map((room, index) => ({ room, number: seed - index * 3 }));
}
