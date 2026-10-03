import { listRoles, listPermissions } from 'feature/Roles/api';
import { problemTitle } from 'feature/Auth/problem';
import * as usersApi from '../api';
import {
  ADMIN_USERS_LIST_REQUEST, ADMIN_USERS_LIST_SUCCESS, ADMIN_USERS_LIST_FAILURE, ADMIN_USERS_SELECT, ADMIN_USER_UPDATED,
  ADMIN_USER_DETAIL_REQUEST, ADMIN_USER_DETAIL_SUCCESS, ADMIN_USER_DETAIL_FAILURE,
  ADMIN_ROLE_OPTIONS_SUCCESS, ADMIN_ROLE_OPTIONS_FAILURE, ADMIN_PERMISSIONS_SUCCESS, ADMIN_PERMISSIONS_FAILURE,
} from './reducer';

// requestId: chỉ phản hồi của lần gọi mới nhất được ghi vào state (bỏ response tìm kiếm/chi tiết cũ).
let listRequestId = 0;
let detailRequestId = 0;

// Lỗi tải được lưu vào state để UI hiển thị (kèm nút Thử lại); không ném lại.
export const loadUsers = params => async (dispatch) => {
  listRequestId += 1;
  const requestId = listRequestId;
  dispatch({ type: ADMIN_USERS_LIST_REQUEST });
  try {
    const data = await usersApi.listUsers(params);
    if (requestId === listRequestId) dispatch({ type: ADMIN_USERS_LIST_SUCCESS, payload: data });
    return data;
  } catch (error) {
    if (requestId === listRequestId) dispatch({ type: ADMIN_USERS_LIST_FAILURE, payload: problemTitle(error) });
    return null;
  }
};

export const loadUserDetail = id => async (dispatch) => {
  detailRequestId += 1;
  const requestId = detailRequestId;
  dispatch({ type: ADMIN_USER_DETAIL_REQUEST, payload: id });
  try {
    const data = await usersApi.getUser(id);
    if (requestId === detailRequestId) dispatch({ type: ADMIN_USER_DETAIL_SUCCESS, payload: data });
    return data;
  } catch (error) {
    if (requestId === detailRequestId) dispatch({ type: ADMIN_USER_DETAIL_FAILURE, payload: { id, message: problemTitle(error) } });
    return null;
  }
};

// Chọn user: chỉ mở chi tiết sau khi GET v1/users/{id} thành công (không dựng panel từ summary thiếu trường).
export const selectUser = id => (dispatch) => {
  dispatch({ type: ADMIN_USERS_SELECT, payload: id });
  if (id) return dispatch(loadUserDetail(id));
  detailRequestId += 1;
  return Promise.resolve(null);
};

export const loadRoleOptions = () => async (dispatch) => {
  try {
    dispatch({ type: ADMIN_ROLE_OPTIONS_SUCCESS, payload: await listRoles() });
  } catch (error) {
    dispatch({ type: ADMIN_ROLE_OPTIONS_FAILURE, payload: problemTitle(error) });
  }
};

export const loadPermissionCatalog = () => async (dispatch) => {
  try {
    dispatch({ type: ADMIN_PERMISSIONS_SUCCESS, payload: await listPermissions() });
  } catch (error) {
    dispatch({ type: ADMIN_PERMISSIONS_FAILURE, payload: problemTitle(error) });
  }
};

// Tạo tài khoản: mật khẩu ban đầu chỉ trả về cho dialog, không đi qua Redux. Không tự retry (không có Idempotency-Key).
export const createUser = payload => () => usersApi.createUser(payload);

// Các mutation ném lỗi cho component hiển thị (code/message từ Problem Details). Không tự retry.
const mutation = call => (...args) => async (dispatch) => {
  const user = await call(...args);
  dispatch({ type: ADMIN_USER_UPDATED, payload: user });
  return user;
};

export const assignRoles = mutation((...args) => usersApi.assignRoles(...args));
export const grantPermission = mutation((...args) => usersApi.grantPermission(...args));
export const revokePermission = mutation((...args) => usersApi.revokePermission(...args));
export const activateUser = mutation((...args) => usersApi.activateUser(...args));
export const deactivateUser = mutation((...args) => usersApi.deactivateUser(...args));
