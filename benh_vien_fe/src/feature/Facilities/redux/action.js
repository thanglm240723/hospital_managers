import * as facilitiesApi from '../api';
import { FACILITIES_LOAD_REQUEST, FACILITIES_LOAD_SUCCESS, FACILITIES_LOAD_FAILURE } from './reducer';
import { problemTitle } from '../../Auth/problem';

// Lỗi tải lưu vào state để UI hiển thị + "Thử lại"; không ném lại.
export const loadFacilities = () => async (dispatch) => {
  dispatch({ type: FACILITIES_LOAD_REQUEST });
  try {
    const data = await facilitiesApi.getFacilityTree();
    dispatch({ type: FACILITIES_LOAD_SUCCESS, payload: data });
    return data;
  } catch (error) {
    dispatch({ type: FACILITIES_LOAD_FAILURE, payload: problemTitle(error) });
    return null;
  }
};

// Mutation ném lỗi cho dialog hiển thị; thành công thì nạp lại cả cây. Không tự retry.
const thenReload = apiCall => (...args) => async (dispatch) => {
  const result = await apiCall(...args);
  await dispatch(loadFacilities());
  return result;
};

export const createBranch = thenReload((...a) => facilitiesApi.createBranch(...a));
export const createDepartment = thenReload((...a) => facilitiesApi.createDepartment(...a));
export const createRoom = thenReload((...a) => facilitiesApi.createRoom(...a));
export const updateFacility = thenReload((...a) => facilitiesApi.updateFacility(...a));
