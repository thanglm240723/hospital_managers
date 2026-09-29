import * as rolesApi from '../api';
import {
  ROLES_LIST_REQUEST, ROLES_LIST_SUCCESS, ROLES_LIST_FAILURE, ROLES_SELECT, ROLES_UPSERT,
} from './reducer';
import { problemTitle } from '../../Auth/problem';

export const selectRole = id => ({ type: ROLES_SELECT, payload: id });

export const loadRoles = () => async (dispatch) => {
  dispatch({ type: ROLES_LIST_REQUEST });
  try {
    const data = await rolesApi.listRoles();
    dispatch({ type: ROLES_LIST_SUCCESS, payload: data });
    return data;
  } catch (error) {
    dispatch({ type: ROLES_LIST_FAILURE, payload: problemTitle(error) });
    throw error;
  }
};

export const createRole = payload => async (dispatch) => {
  const role = await rolesApi.createRole(payload);
  dispatch({ type: ROLES_UPSERT, payload: role });
  return role;
};

export const updateRolePermissions = (id, codes) => async (dispatch) => {
  const role = await rolesApi.updateRolePermissions(id, codes);
  dispatch({ type: ROLES_UPSERT, payload: role });
  return role;
};
