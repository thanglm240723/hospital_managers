// Hợp đồng DỰ KIẾN — chưa có ở BE (module ReceptionQueue/Clinical).
import http from 'service/http';

const data = response => response.data;

export const listQueue = () => http.get('v1/clinic/queue').then(data);

// Thứ tự hàng chờ và gọi lượt do server quyết định (NV-10/11); không có API ghi đè cả hàng từ client.
// CẦN_XÁC_NHẬN: thay bằng command call-next/recall/mark-absent/return khi có hợp đồng BE.
export const saveQueue = () => Promise.reject(new Error('Chưa có hợp đồng BE cho thao tác hàng chờ.'));

export const requestVitals = id => http.post(`v1/encounters/${id}/request-vitals`).then(data);
export const getEncounter = id => http.get(`v1/encounters/${id}`).then(data);
export const saveEncounterForm = (id, fields) => http.put(`v1/encounters/${id}/form`, fields).then(data);
export const addOrder = (id, orderCode) => http.post(`v1/encounters/${id}/orders`, { orderCode }).then(data);
export const addPrescription = (id, item) => http.post(`v1/encounters/${id}/prescriptions`, item).then(data);
export const endEarly = (id, reason) => http.post(`v1/encounters/${id}/end-early`, { reason }).then(data);
export const waitForLabResult = id => http.post(`v1/encounters/${id}/wait-for-results`).then(data);
export const admitPatient = id => http.post(`v1/encounters/${id}/admit`).then(data);
export const confirmEncounter = id => http.post(`v1/encounters/${id}/confirm`).then(data);
export const orderCatalog = () => http.get('v1/catalog/services').then(data);
export const medicineCatalog = () => http.get('v1/catalog/medicines').then(data);
