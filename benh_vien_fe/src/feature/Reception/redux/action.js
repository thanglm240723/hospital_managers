import * as receptionApi from '../api';
import { RECEPTION_SEARCH_RESULTS, RECEPTION_VISIT_REGISTERED, RECEPTION_VISIT_CLEARED } from './reducer';

export const searchPatients = params => async (dispatch) => {
  const items = await receptionApi.searchPatients(params);
  dispatch({ type: RECEPTION_SEARCH_RESULTS, payload: items });
  return items;
};

export const checkDuplicates = payload => () => receptionApi.checkDuplicates(payload);
export const createPatient = payload => () => receptionApi.createPatient(payload);
export const getPatient = id => () => receptionApi.getPatient(id);
export const getOpenVisits = id => () => receptionApi.getOpenVisits(id);
export const listDepartmentSessions = () => () => receptionApi.listDepartmentSessions();

export const registerVisit = (payload, idempotencyKey) => async (dispatch) => {
  const visit = await receptionApi.registerVisit(payload, idempotencyKey);
  dispatch({ type: RECEPTION_VISIT_REGISTERED, payload: visit });
  return visit;
};

export const transferVisit = (visitId, payload) => () => receptionApi.transferVisit(visitId, payload);
export const clearVisit = () => ({ type: RECEPTION_VISIT_CLEARED });
