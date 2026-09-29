import { mockDelay } from 'service/mockMode';
import { FAKE_NAMES, maskDocumentNumber, maskPhoneNumber, nextMockId } from 'service/mockData';

let patients = FAKE_NAMES.map((name, index) => ({
  id: `patient-${index + 1}`,
  fullName: name,
  gender: index % 2 === 0 ? 'male' : 'female',
  birthDate: `19${60 + index}-0${(index % 9) + 1}-1${index % 9}`,
  birthYearOnly: false,
  address: `Số ${index + 1}, Phường ${index + 1}, TP. Hồ Chí Minh`,
  phone: maskPhoneNumber(`09000000${10 + index}`),
  documentNumber: maskDocumentNumber(`07920000${1000 + index}`),
}));

const openVisitsByPatient = { 'patient-1': [{ id: 'visit-open-1', department: 'Nội tổng quát', openedAt: new Date().toISOString() }] };

export const DEPARTMENT_SESSIONS = [
  {
    id: 'dept-noi',
    department: 'Nội tổng quát',
    sessions: [
      { id: 'sess-noi-sang', label: 'Sáng', doctor: 'BS. Nguyễn Văn An', time: '07:30 - 11:30', waitingCount: 12 },
      { id: 'sess-noi-chieu', label: 'Chiều', doctor: 'BS. Trần Thị Bình', time: '13:30 - 17:00', waitingCount: 5 },
    ],
  },
  {
    id: 'dept-nhi',
    department: 'Nhi',
    sessions: [
      { id: 'sess-nhi-sang', label: 'Sáng', doctor: 'BS. Lê Hoàng Cường', time: '07:30 - 11:30', waitingCount: 8 },
    ],
  },
  {
    id: 'dept-ngoai',
    department: 'Ngoại tổng quát',
    sessions: [
      { id: 'sess-ngoai-sang', label: 'Sáng', doctor: 'BS. Phạm Thị Dung', time: '07:30 - 11:30', waitingCount: 3 },
    ],
  },
];

let queueCounter = 30;

export function searchPatients(params = {}) {
  const term = (params.fullName || params.documentNumber || params.phone || '').toLowerCase();
  let items = patients;
  if (term) {
    items = items.filter(p => p.fullName.toLowerCase().includes(term) || p.phone.includes(term) || p.documentNumber.includes(term));
  }
  if (params.birthDate) items = items.filter(p => p.birthDate === params.birthDate);
  return mockDelay(items);
}

export function checkDuplicates(payload) {
  const term = (payload.fullName || '').toLowerCase().trim();
  const candidates = patients.filter(p => p.fullName.toLowerCase() === term);
  return mockDelay(candidates);
}

export function createPatient(payload) {
  const patient = {
    id: nextMockId('patient'),
    fullName: payload.fullName,
    gender: payload.gender,
    birthDate: payload.birthDate,
    birthYearOnly: payload.birthYearOnly || false,
    address: payload.address,
    phone: maskPhoneNumber(payload.phone),
    documentNumber: payload.documentNumber ? maskDocumentNumber(payload.documentNumber) : null,
  };
  patients = [patient, ...patients];
  return mockDelay(patient);
}

export function getPatient(id) {
  return mockDelay(patients.find(p => p.id === id) || null);
}

export function getOpenVisits(patientId) {
  return mockDelay(openVisitsByPatient[patientId] || []);
}

export function listDepartmentSessions() {
  return mockDelay(DEPARTMENT_SESSIONS);
}

export function registerVisit(payload) {
  queueCounter += 1;
  const visit = {
    id: nextMockId('visit'),
    queueNumber: queueCounter,
    roomLabel: 'Phòng khám 03',
    department: payload.department,
    sessionLabel: payload.sessionLabel,
    doctor: payload.doctor,
    patientName: payload.patientName,
    reason: payload.reason,
    paymentMethod: payload.paymentMethod,
    priorityGroup: payload.priorityGroup,
    createdAt: new Date().toISOString(),
  };
  return mockDelay(visit, 500);
}

export function transferVisit(visitId, payload) {
  return mockDelay({ id: visitId, department: payload.department, reason: payload.reason });
}
