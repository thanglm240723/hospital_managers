import * as usersApi from '../api';
import {
  ADMIN_USERS_LIST_REQUEST, ADMIN_USERS_LIST_SUCCESS, ADMIN_USERS_LIST_FAILURE, ADMIN_USERS_SELECT, ADMIN_USER_UPDATED,
} from './reducer';
import { problemTitle } from '../../Auth/problem';

export const selectUser = id => ({ type: ADMIN_USERS_SELECT, payload: id });

export const loadUsers = params => async (dispatch) => {
  dispatch({ type: ADMIN_USERS_LIST_REQUEST });
  try {
    const data = await usersApi.listUsers(params);
    dispatch({ type: ADMIN_USERS_LIST_SUCCESS, payload: data });
    return data;
  } catch (error) {
    dispatch({ type: ADMIN_USERS_LIST_FAILURE, payload: problemTitle(error) });
    throw error;
  }
};

export const createUser = payload => () => usersApi.createUser(payload);

export const assignRoles = (id, roleIds) => async (dispatch) => {
  const user = await usersApi.assignRoles(id, roleIds);
  dispatch({ type: ADMIN_USER_UPDATED, payload: user });
  return user;
};

export const grantPermissions = (id, codes) => async (dispatch) => {
  const user = await usersApi.grantPermissions(id, codes);
  dispatch({ type: ADMIN_USER_UPDATED, payload: user });
  return user;
};

export const revokePermissions = (id, codes) => async (dispatch) => {
  const user = await usersApi.revokePermissions(id, codes);
  dispatch({ type: ADMIN_USER_UPDATED, payload: user });
  return user;
};

export const resetTemporaryPassword = id => () => usersApi.resetTemporaryPassword(id);

export const activateUser = (id, idempotencyKey) => async (dispatch) => {
  const user = await usersApi.activateUser(id, idempotencyKey);
  dispatch({ type: ADMIN_USER_UPDATED, payload: user });
  return user;
};

export const deactivateUser = (id, idempotencyKey) => async (dispatch) => {
  const user = await usersApi.deactivateUser(id, idempotencyKey);
  dispatch({ type: ADMIN_USER_UPDATED, payload: user });
  return user;
};
