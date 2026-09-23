export const SHOW_LOADING = 'HMS/LOADING/SHOW';
export const HIDE_LOADING = 'HMS/LOADING/HIDE';

export const showLoading = () => ({ type: SHOW_LOADING });
export const hideLoading = () => ({ type: HIDE_LOADING });

// Wraps a promise-returning fn: show spinner -> run -> hide spinner (also on error).
export const loadingAction = fn => async dispatch => {
  dispatch(showLoading());
  try {
    return await fn();
  } finally {
    dispatch(hideLoading());
  }
};
