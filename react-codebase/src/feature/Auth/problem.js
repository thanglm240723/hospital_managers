// Đọc Problem Details (RFC 9457) từ lỗi axios.
export const problemTitle = error => (
  error && error.response && error.response.data && error.response.data.title
) || 'Không kết nối được máy chủ. Vui lòng thử lại.';

export const problemFieldErrors = error => (
  error && error.response && error.response.data && error.response.data.errors
) || {};
