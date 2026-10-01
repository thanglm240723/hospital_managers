import { clearAccessToken } from './tokenStore';

// Điều phối vòng đời phiên trong tab — KHÔNG cấp quyền (server vẫn xác thực token/quyền).
// Mỗi lần login/khởi tạo tường minh hoặc logout đổi epoch; kết quả bất đồng bộ (login, /me, refresh, đổi mật khẩu)
// chỉ được áp dụng khi epoch lúc bắt đầu vẫn là epoch hiện tại.
let epoch = 0;
// Tab chỉ nhận token do tab khác phát khi đang có phiên được khởi tạo tường minh (login/boot).
// Sau logout bỏ qua mọi token tự phát cho tới lần khởi tạo/login tiếp theo.
let acceptingTokens = false;

// Thế hệ phiên dùng chung giữa các tab (cookie refresh dùng chung): login thành công hoặc logout tăng số này.
// Chỉ là bộ đếm KHÔNG bí mật trong localStorage — không phải token, không phải dữ liệu bệnh án.
// Thông điệp BroadcastChannel mang thế hệ này; tab ở thế hệ khác (đã logout, tài khoản khác) bỏ qua.
const SESSION_GEN_KEY = 'auth:sessionGen';
let mySessionGen = null;

// localStorage lỗi/không có: mọi tab coi như thế hệ 0 (quay về hành vi chỉ dựa vào epoch trong tab).
function readSharedGen() {
  try {
    if (typeof localStorage === 'undefined') return 0;
    const parsed = parseInt(localStorage.getItem(SESSION_GEN_KEY), 10);
    return Number.isFinite(parsed) ? parsed : 0;
  } catch {
    return 0;
  }
}

function writeSharedGen(value) {
  try {
    if (typeof localStorage !== 'undefined') localStorage.setItem(SESSION_GEN_KEY, String(value));
  } catch {
    // best effort
  }
}

export function getSessionEpoch() {
  return epoch;
}

// Boot/login bắt đầu: tab gia nhập thế hệ phiên dùng chung hiện tại.
export function beginSessionTransition() {
  epoch += 1;
  acceptingTokens = true;
  mySessionGen = readSharedGen();
  return epoch;
}

// Login thành công: cookie đã thuộc phiên mới ⇒ mở thế hệ mới, tab khác (phiên cũ) không nhận token của tab này.
export function claimSharedSession() {
  writeSharedGen(readSharedGen() + 1);
  mySessionGen = readSharedGen();
}

export function isCurrentSharedSession() {
  return mySessionGen !== null && readSharedGen() === mySessionGen;
}

// Logout chủ động của tab đang ở thế hệ hiện tại: kết thúc thế hệ để refresh đến muộn ở mọi tab bị bỏ.
// Tab đã lỗi thời (tài khoản khác đã đăng nhập) không được làm hỏng thế hệ của phiên mới.
export function endSharedSession() {
  if (isCurrentSharedSession()) writeSharedGen(mySessionGen + 1);
}

export function invalidateLocalSession() {
  epoch += 1;
  acceptingTokens = false;
  mySessionGen = null;
  clearAccessToken();
}

export function isCurrentSessionEpoch(value) {
  return value === epoch;
}

export function isAcceptingTokens() {
  return acceptingTokens;
}

export function getSessionGeneration() {
  return mySessionGen;
}
