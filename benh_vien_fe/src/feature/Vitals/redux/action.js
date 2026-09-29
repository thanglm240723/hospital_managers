import * as vitalsApi from '../api';
import {
  VITALS_QUEUE_REQUEST, VITALS_QUEUE_SUCCESS, VITALS_QUEUE_FAILURE, VITALS_CALL_NEXT, VITALS_COMPLETED, VITALS_CLEAR,
} from './reducer';
import { problemTitle } from '../../Auth/problem';

export const callNextVitals = () => ({ type: VITALS_CALL_NEXT });
export const clearVitals = () => ({ type: VITALS_CLEAR });

export const loadVitalsQueue = () => async (dispatch) => {
  dispatch({ type: VITALS_QUEUE_REQUEST });
  try {
    const data = await vitalsApi.listVitalsQueue();
    dispatch({ type: VITALS_QUEUE_SUCCESS, payload: data });
    return data;
  } catch (error) {
    dispatch({ type: VITALS_QUEUE_FAILURE, payload: problemTitle(error) });
    throw error;
  }
};

// Không tự retry: command chưa có Idempotency-Key.
export const completeVitals = (ticketId, measurements) => async (dispatch) => {
  await vitalsApi.completeVitals(ticketId, measurements);
  dispatch({ type: VITALS_COMPLETED });
  return dispatch(loadVitalsQueue());
};
