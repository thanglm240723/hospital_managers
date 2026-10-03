import { combineReducers } from 'redux';
import { reducer as form } from 'redux-form';

// IMPORT_STORES
import { loadingReducer } from './feature/Loading';
import { authReducer, AUTH_LOGGED_OUT } from './feature/Auth';
import { workspaceReducer } from './feature/Workspace';
import { adminReducer } from './feature/Admin';
import { rolesReducer } from './feature/Roles';
import { facilitiesReducer } from './feature/Facilities';
import { receptionReducer } from './feature/Reception';
import { clinicReducer } from './feature/Clinic';
import { vitalsReducer } from './feature/Vitals';

const appReducer = combineReducers({
  // CONNECT_STORES
  auth: authReducer,
  workspace: workspaceReducer,
  admin: adminReducer,
  roles: rolesReducer,
  facilities: facilitiesReducer,
  reception: receptionReducer,
  clinic: clinicReducer,
  vitals: vitalsReducer,
  form,
  loadingModal: loadingReducer,
});

export default (state, action) => appReducer(action.type === AUTH_LOGGED_OUT ? undefined : state, action);
