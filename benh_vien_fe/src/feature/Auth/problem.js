// Đọc Problem Details (RFC 9457) từ lỗi axios.
export const problemTitle = error => (
  error && error.response && error.response.data && error.response.data.title
) || 'Không kết nối được máy chủ. Vui lòng thử lại.';

export const problemFieldErrors = error => (
  error && error.response && error.response.data && error.response.data.errors
) || {};

// Số giây trong header Retry-After (429), null nếu không có.
export const retryAfterSeconds = (error) => {
  const value = error && error.response && error.response.headers
    && error.response.headers['retry-after'];
  const seconds = parseInt(value, 10);
  return Number.isNaN(seconds) ? null : seconds;
};
