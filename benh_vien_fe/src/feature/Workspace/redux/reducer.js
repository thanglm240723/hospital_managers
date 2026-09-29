export const WORKSPACE_SELECTED = 'HMS/WORKSPACE/SELECTED';
export const WORKSPACE_CLEARED = 'HMS/WORKSPACE/CLEARED';

const initialState = { selectedId: null };

export default function workspaceReducer(state = initialState, action) {
  switch (action.type) {
    case WORKSPACE_SELECTED:
      return { ...state, selectedId: action.payload };
    case WORKSPACE_CLEARED:
      return { ...initialState };
    default:
      return state;
  }
}
