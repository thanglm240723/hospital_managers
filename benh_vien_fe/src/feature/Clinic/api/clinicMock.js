import { mockDelay } from 'service/mockMode';
import { FAKE_NAMES } from 'service/mockData';

let seq = 0;
const now = () => Date.now();

function mkItem(group, backReason, ageOffset) {
  seq += 1;
  const name = FAKE_NAMES[seq % FAKE_NAMES.length];
  return {
    id: `q-${seq}`,
    queueNumber: 20 + seq,
    fullName: name,
    birthDate: `19${60 + ageOffset}-01-01`,
    group,
    backReason: backReason || null,
    status: 'waiting',
    enqueuedAt: now() - ageOffset * 60000,
    callAttempt: 0,
    callSecondsLeft: null,
  };
}

let queue = [
  mkItem('normal', null, 5),
  mkItem('priority', null, 3),
  mkItem('back', 'results', 2),
  mkItem('normal', null, 8),
];

const lateList = [];

export function listQueue() {
  return mockDelay({ queue: [...queue], late: [...lateList] });
}

export function saveQueue(nextQueue, nextLate) {
  queue = nextQueue;
  if (nextLate) lateList.splice(0, lateList.length, ...nextLate);
  return mockDelay({ queue: [...queue], late: [...lateList] });
}

export function requestVitals(id) {
  queue = queue.map(i => (i.id === id ? { ...i, status: 'sent_to_vitals' } : i));
  return mockDelay(queue.find(i => i.id === id));
}

const ORDER_CATALOG = [
  { code: 'XN001', name: 'Công thức máu' },
  { code: 'XN002', name: 'Sinh hóa máu' },
  { code: 'CDHA001', name: 'X-quang ngực thẳng' },
  { code: 'CDHA002', name: 'Siêu âm ổ bụng' },
];

const MEDICINE_CATALOG = [
  { code: 'TH001', name: 'Paracetamol 500mg', unit: 'Viên' },
  { code: 'TH002', name: 'Amoxicillin 500mg', unit: 'Viên' },
  { code: 'TH003', name: 'Oresol', unit: 'Gói' },
];

const encounters = {};

function getOrCreateEncounter(id) {
  if (!encounters[id]) {
    const patient = queue.find(q => q.id === id) || {
      fullName: FAKE_NAMES[0], birthDate: '1970-01-01', queueNumber: 0,
    };
    encounters[id] = {
      id,
      patient: { fullName: patient.fullName, birthDate: patient.birthDate, allergies: 'Không ghi nhận dị ứng.' },
      clinicalStatus: 'examining',
      recordStatus: 'draft',
      reason: '',
      history: '',
      pastHistory: '',
      examination: '',
      diagnosis: '',
      conclusion: '',
      plan: '',
      orders: [],
      prescriptions: [],
      vitals: null,
    };
  }
  return encounters[id];
}

export function getEncounter(id) {
  return mockDelay(getOrCreateEncounter(id));
}

export function saveEncounterForm(id, fields) {
  const encounter = getOrCreateEncounter(id);
  Object.assign(encounter, fields);
  return mockDelay(encounter);
}

export function addOrder(id, orderCode) {
  const encounter = getOrCreateEncounter(id);
  const catalogItem = ORDER_CATALOG.find(o => o.code === orderCode);
  encounter.orders = [...encounter.orders, {
    id: `order-${encounter.orders.length + 1}`,
    code: catalogItem.code,
    name: catalogItem.name,
    performStatus: 'pending',
    paymentStatus: 'unpaid',
    resultStatus: 'pending',
  }];
  return mockDelay(encounter);
}

export function addPrescription(id, item) {
  const encounter = getOrCreateEncounter(id);
  encounter.prescriptions = [...encounter.prescriptions, { id: `rx-${encounter.prescriptions.length + 1}`, ...item }];
  return mockDelay(encounter);
}

export function endEarly(id, reason) {
  const encounter = getOrCreateEncounter(id);
  encounter.clinicalStatus = 'ended_early';
  encounter.endEarlyReason = reason;
  return mockDelay(encounter);
}

export function waitForLabResult(id) {
  const encounter = getOrCreateEncounter(id);
  encounter.clinicalStatus = 'waiting_result';
  return mockDelay(encounter);
}

export function admitPatient(id) {
  const encounter = getOrCreateEncounter(id);
  encounter.clinicalStatus = 'admission_ordered';
  return mockDelay(encounter);
}

export function confirmEncounter(id) {
  const encounter = getOrCreateEncounter(id);
  encounter.clinicalStatus = 'completed';
  encounter.recordStatus = 'confirmed';
  return mockDelay(encounter);
}

export function orderCatalog() {
  return mockDelay(ORDER_CATALOG);
}

export function medicineCatalog() {
  return mockDelay(MEDICINE_CATALOG);
}
