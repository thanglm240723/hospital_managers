import * as rolesApi from '../api';
import {
  ROLES_LIST_REQUEST, ROLES_LIST_SUCCESS, ROLES_LIST_FAILURE, ROLES_SELECT, ROLES_UPSERT,
  PERMISSIONS_LIST_SUCCESS, PERMISSIONS_LIST_FAILURE,
} from './reducer';
import { problemTitle } from '../../Auth/problem';

export const selectRole = id => ({ type: ROLES_SELECT, payload: id });

// Lỗi tải được lưu vào state để UI hiển thị; không ném lại nên không có promise rejection bị bỏ sót.
export const loadRoles = () => async (dispatch) => {
  dispatch({ type: ROLES_LIST_REQUEST });
  try {
    const data = await rolesApi.listRoles();
    dispatch({ type: ROLES_LIST_SUCCESS, payload: data });
    return data;
  } catch (error) {
    dispatch({ type: ROLES_LIST_FAILURE, payload: problemTitle(error) });
    return null;
  }
};

export const loadPermissions = () => async (dispatch) => {
  try {
    const data = await rolesApi.listPermissions();
    dispatch({ type: PERMISSIONS_LIST_SUCCESS, payload: data });
  } catch (error) {
    dispatch({ type: PERMISSIONS_LIST_FAILURE, payload: problemTitle(error) });
  }
};

// Các mutation ném lỗi cho component hiển thị (code/message từ Problem Details). Không tự retry.
export const createRole = payload => async (dispatch) => {
  const role = await rolesApi.createRole(payload);
  dispatch({ type: ROLES_UPSERT, payload: role });
  return role;
};

export const renameRole = (id, name, version) => async (dispatch) => {
  const role = await rolesApi.renameRole(id, name, version);
  dispatch({ type: ROLES_UPSERT, payload: role });
  return role;
};

export const updateRolePermissions = (id, codes, version) => async (dispatch) => {
  const role = await rolesApi.updateRolePermissions(id, codes, version);
  dispatch({ type: ROLES_UPSERT, payload: role });
  return role;
};
