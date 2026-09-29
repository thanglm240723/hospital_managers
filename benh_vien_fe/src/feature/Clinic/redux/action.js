import * as clinicApi from '../api';
import { CLINIC_ENCOUNTER_LOADED, CLINIC_ENCOUNTER_UPDATED, CLINIC_ENCOUNTER_CLEARED } from './reducer';

export const loadEncounter = id => async (dispatch) => {
  const encounter = await clinicApi.getEncounter(id);
  dispatch({ type: CLINIC_ENCOUNTER_LOADED, payload: encounter });
  return encounter;
};

const updated = dispatch => encounter => dispatch({ type: CLINIC_ENCOUNTER_UPDATED, payload: encounter });

export const saveEncounterForm = (id, fields) => async (dispatch) => {
  const encounter = await clinicApi.saveEncounterForm(id, fields);
  updated(dispatch)(encounter);
  return encounter;
};

export const addOrder = (id, code) => async (dispatch) => {
  const encounter = await clinicApi.addOrder(id, code);
  updated(dispatch)(encounter);
  return encounter;
};

export const addPrescription = (id, item) => async (dispatch) => {
  const encounter = await clinicApi.addPrescription(id, item);
  updated(dispatch)(encounter);
  return encounter;
};

export const requestVitals = id => async (dispatch) => {
  await clinicApi.requestVitals(id);
  return dispatch(loadEncounter(id));
};

export const endEarly = (id, reason) => async (dispatch) => {
  const encounter = await clinicApi.endEarly(id, reason);
  updated(dispatch)(encounter);
  return encounter;
};

export const waitForLabResult = id => async (dispatch) => {
  const encounter = await clinicApi.waitForLabResult(id);
  updated(dispatch)(encounter);
  return encounter;
};

export const admitPatient = id => async (dispatch) => {
  const encounter = await clinicApi.admitPatient(id);
  updated(dispatch)(encounter);
  return encounter;
};

export const confirmEncounter = id => async (dispatch) => {
  const encounter = await clinicApi.confirmEncounter(id);
  updated(dispatch)(encounter);
  return encounter;
};

export const clearEncounter = () => ({ type: CLINIC_ENCOUNTER_CLEARED });

export const loadOrderCatalog = () => () => clinicApi.orderCatalog();
export const loadMedicineCatalog = () => () => clinicApi.medicineCatalog();
