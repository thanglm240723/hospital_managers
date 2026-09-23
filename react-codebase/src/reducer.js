import { combineReducers } from 'redux';
import { reducer as form } from 'redux-form';

// IMPORT_STORES
import { loadingReducer } from './feature/Loading';
import { authReducer, AUTH_LOGGED_OUT } from './feature/Auth';

const appReducer = combineReducers({
  // CONNECT_STORES
  auth: authReducer,
  form,
  loadingModal: loadingReducer,
});

export default (state, action) => appReducer(action.type === AUTH_LOGGED_OUT ? undefined : state, action);
