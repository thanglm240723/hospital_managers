import { clearAccessToken } from './tokenStore';

// Điều phối vòng đời phiên trong tab — KHÔNG cấp quyền (server vẫn xác thực token/quyền).
// Mỗi lần login/khởi tạo tường minh hoặc logout đổi epoch; kết quả bất đồng bộ (login, /me, refresh)
// chỉ được áp dụng khi epoch lúc bắt đầu vẫn là epoch hiện tại.
let epoch = 0;
// Tab chỉ nhận token do tab khác phát khi đang có phiên được khởi tạo tường minh (login/boot).
// Sau logout bỏ qua mọi token tự phát cho tới lần khởi tạo/login tiếp theo.
let acceptingTokens = false;

export function getSessionEpoch() {
  return epoch;
}

export function beginSessionTransition() {
  epoch += 1;
  acceptingTokens = true;
  return epoch;
}

export function invalidateLocalSession() {
  epoch += 1;
  acceptingTokens = false;
  clearAccessToken();
}

export function isCurrentSessionEpoch(value) {
  return value === epoch;
}

export function isAcceptingTokens() {
  return acceptingTokens;
}
