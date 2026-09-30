import http from '../../../service/http';
import * as authClient from '../api/authClient';
import { refreshAccessToken, broadcastLogout, cancelPendingTokenRequests } from '../session/refreshCoordinator';
import { beginSessionTransition, getSessionEpoch, invalidateLocalSession, isCurrentSessionEpoch } from '../session/sessionLifecycle';
import { setAccessToken, clearAccessToken } from '../session/tokenStore';
import {
  AUTH_BOOTING, AUTH_AUTHENTICATED, AUTH_ANONYMOUS, AUTH_LOGGED_OUT,
} from './actionTypes';

export const SESSION_EXPIRED_MESSAGE = 'Phiên đăng nhập đã hết hiệu lực. Vui lòng đăng nhập lại.';
export const LOGOUT_UNCONFIRMED_MESSAGE = 'Đã thoát trên thiết bị này; chưa xác nhận thu hồi phiên trên máy chủ.';

// epoch: phiên lúc bắt đầu gọi. Nếu phiên đã kết thúc (logout) khi /me trả về thì bỏ qua kết quả.
export const loadMe = (epoch = getSessionEpoch()) => async (dispatch) => {
  const { data } = await http.get('v1/auth/me');
  if (!isCurrentSessionEpoch(epoch)) return null;
  dispatch({ type: AUTH_AUTHENTICATED, payload: data });
  return data;
};

// Khởi động/F5: không có token trong RAM ⇒ thử refresh bằng cookie.
export const bootAuth = () => async (dispatch) => {
  const epoch = beginSessionTransition();
  dispatch({ type: AUTH_BOOTING });
  try {
    await refreshAccessToken();
    await dispatch(loadMe(epoch));
  } catch (error) {
    if (!isCurrentSessionEpoch(epoch)) return;
    clearAccessToken();
    dispatch({ type: AUTH_ANONYMOUS });
  }
};

export const login = (email, password) => async (dispatch) => {
  const epoch = beginSessionTransition();
  const data = await authClient.login(email, password);
  // Logout/đăng nhập khác xảy ra trong lúc chờ: không áp dụng token đến muộn.
  if (!isCurrentSessionEpoch(epoch)) return null;
  setAccessToken(data.accessToken, data.expiresAtUtc);
  return dispatch(loadMe(epoch));
};

function endLocalSession() {
  cancelPendingTokenRequests();
  invalidateLocalSession();
}

// Xoá phiên cục bộ ngay (token RAM + toàn bộ state qua AUTH_LOGGED_OUT) rồi báo server đúng một lần.
// Server lỗi: không retry, không báo thu hồi thành công giả.
export const logout = message => async (dispatch) => {
  endLocalSession();
  const epoch = getSessionEpoch();
  dispatch({ type: AUTH_LOGGED_OUT, payload: message || null });
  broadcastLogout();
  try {
    await authClient.logout();
  } catch (error) {
    // Không log lỗi (có thể chứa header/cookie). Chỉ báo nếu người dùng chưa đăng nhập lại.
    if (isCurrentSessionEpoch(epoch)) dispatch({ type: AUTH_LOGGED_OUT, payload: LOGOUT_UNCONFIRMED_MESSAGE });
  }
};

export const expireSession = () => (dispatch) => {
  endLocalSession();
  dispatch({ type: AUTH_LOGGED_OUT, payload: SESSION_EXPIRED_MESSAGE });
  broadcastLogout();
};

// Chỉ đổi mật khẩu + giữ token mới trong RAM của tab này. Không tự gọi loadMe: gọi thất bại (mất
// mạng, 401…) không được hiểu nhầm là đổi mật khẩu thất bại và không được kích hoạt POST lại.
export const changePassword = (currentPassword, newPassword) => async () => {
  const { data } = await http.post('v1/auth/change-password', { currentPassword, newPassword });
  setAccessToken(data.accessToken, data.expiresAtUtc);
  return data;
};
