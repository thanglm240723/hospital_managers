// Hợp đồng DỰ KIẾN — chưa có ở BE (module ReceptionQueue/Clinical).
import http from 'service/http';

export const listVitalsQueue = () => http.get('v1/vitals/queue').then(response => response.data);
export const completeVitals = (ticketId, measurements) => http
  .post(`v1/vitals/${ticketId}/complete`, measurements)
  .then(response => response.data);
