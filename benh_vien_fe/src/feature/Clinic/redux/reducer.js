export const CLINIC_ENCOUNTER_LOADED = 'HMS/CLINIC/ENCOUNTER_LOADED';
export const CLINIC_ENCOUNTER_UPDATED = 'HMS/CLINIC/ENCOUNTER_UPDATED';
export const CLINIC_ENCOUNTER_CLEARED = 'HMS/CLINIC/ENCOUNTER_CLEARED';

const initialState = { current: null };

// Bệnh án đang khám chỉ giữ tạm trong Redux khi ở màn /clinic/encounters/:id — xóa khi rời màn (CLINIC_ENCOUNTER_CLEARED).
export default function clinicReducer(state = initialState, action) {
  switch (action.type) {
    case CLINIC_ENCOUNTER_LOADED:
    case CLINIC_ENCOUNTER_UPDATED:
      return { ...state, current: action.payload };
    case CLINIC_ENCOUNTER_CLEARED:
      return { ...initialState };
    default:
      return state;
  }
}
