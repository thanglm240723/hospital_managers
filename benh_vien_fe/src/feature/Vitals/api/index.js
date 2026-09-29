import { isMockApiEnabled } from 'service/mockMode';
import * as real from './vitalsClient';
import * as mock from './vitalsMock';

const impl = isMockApiEnabled() ? mock : real;

export const { listVitalsQueue, completeVitals } = impl;
