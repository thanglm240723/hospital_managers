import { isMockApiEnabled } from 'service/mockMode';
import * as real from './clinicClient';
import * as mock from './clinicMock';

const impl = isMockApiEnabled() ? mock : real;

export const {
  listQueue, saveQueue, requestVitals, getEncounter, saveEncounterForm, addOrder, addPrescription,
  endEarly, waitForLabResult, admitPatient, confirmEncounter, orderCatalog, medicineCatalog,
} = impl;
