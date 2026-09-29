// Hợp đồng DỰ KIẾN — chưa có ở BE. CẦN_XÁC_NHẬN khi module PatientRegistry/ReceptionQueue triển khai.
import http from 'service/http';

export const searchPatients = params => http.get('v1/patients', { params }).then(response => response.data);
export const checkDuplicates = payload => http.post('v1/patients/check-duplicates', payload).then(response => response.data);
export const createPatient = payload => http.post('v1/patients', payload).then(response => response.data);
export const getPatient = id => http.get(`v1/patients/${id}`).then(response => response.data);
export const getOpenVisits = patientId => http.get(`v1/patients/${patientId}/open-visits`).then(response => response.data);
export const listDepartmentSessions = () => http.get('v1/reception/department-sessions').then(response => response.data);

export const registerVisit = (payload, idempotencyKey) => http
  .post('v1/reception/visits', payload, { headers: { 'Idempotency-Key': idempotencyKey } })
  .then(response => response.data);

export const transferVisit = (visitId, payload) => http
  .post(`v1/reception/visits/${visitId}/transfer`, payload)
  .then(response => response.data);
