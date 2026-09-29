import { SHOW_LOADING, HIDE_LOADING } from './action';

// Counter so overlapping requests keep the spinner until the last one finishes.
const initialState = { count: 0 };

export const loadingReducer = (state = initialState, action) => {
  switch (action.type) {
    case SHOW_LOADING:
      return { count: state.count + 1 };
    case HIDE_LOADING:
      return { count: Math.max(0, state.count - 1) };
    default:
      return state;
  }
};
