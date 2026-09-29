import { isMockApiEnabled } from 'service/mockMode';
import * as real from './receptionClient';
import * as mock from './receptionMock';

const impl = isMockApiEnabled() ? mock : real;

export const {
  searchPatients, checkDuplicates, createPatient, getPatient, getOpenVisits, listDepartmentSessions,
  registerVisit, transferVisit,
} = impl;
