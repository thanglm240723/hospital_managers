// Sinh Idempotency-Key cho command ghi dữ liệu — sinh 1 lần/lần submit, giữ nguyên khi người dùng bấm lại do lỗi mạng.
export function newIdempotencyKey() {
  if (window.crypto && window.crypto.randomUUID) return window.crypto.randomUUID();
  // Dự phòng cho môi trường test/trình duyệt cũ không có crypto.randomUUID.
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0; // eslint-disable-line no-bitwise
    const v = c === 'x' ? r : (r & 0x3) | 0x8; // eslint-disable-line no-bitwise
    return v.toString(16);
  });
}
