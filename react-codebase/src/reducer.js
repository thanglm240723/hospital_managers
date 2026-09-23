import { combineReducers } from 'redux';
import { reducer as form } from 'redux-form';

// IMPORT_STORES
import { loadingReducer } from './feature/Loading';

export default combineReducers({
  // CONNECT_STORES
  form,
  loadingModal: loadingReducer,
});
