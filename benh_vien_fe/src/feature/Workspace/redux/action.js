import { push } from 'connected-react-router';
import { getDefaultRoute } from '../routeAccess';
import { FEATURE_AVAILABILITY } from '../availability';
import { WORKSPACE_SELECTED, WORKSPACE_CLEARED } from './reducer';

export const selectWorkspace = id => ({ type: WORKSPACE_SELECTED, payload: id });
export const clearWorkspace = () => ({ type: WORKSPACE_CLEARED });

// Đổi khu vực từ sidebar: điều hướng tới màn mặc định của khu vực mới rồi ghi lựa chọn.
export const switchWorkspace = id => (dispatch, getState) => {
  const target = getDefaultRoute(id, getState().auth.permissions, FEATURE_AVAILABILITY);
  if (!target) return;
  dispatch(push(target));
  dispatch(selectWorkspace(id));
};
