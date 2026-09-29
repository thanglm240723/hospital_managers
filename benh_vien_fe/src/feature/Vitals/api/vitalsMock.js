import { mockDelay } from 'service/mockMode';
import { FAKE_NAMES } from 'service/mockData';

let queue = [1, 2, 3].map(n => ({
  id: `vt-${n}`, queueNumber: 60 + n, fullName: FAKE_NAMES[n], birthDate: `1975-0${n}-10`,
}));

export function listVitalsQueue() {
  return mockDelay([...queue]);
}

export function completeVitals(id) {
  queue = queue.filter(i => i.id !== id);
  return mockDelay({ id });
}
