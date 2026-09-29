import { WORKSPACE_SELECTED, WORKSPACE_CLEARED } from './reducer';

export const selectWorkspace = id => ({ type: WORKSPACE_SELECTED, payload: id });
export const clearWorkspace = () => ({ type: WORKSPACE_CLEARED });
