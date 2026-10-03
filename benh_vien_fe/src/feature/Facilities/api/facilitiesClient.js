import http from 'service/http';

// Hợp đồng BE (plan 01 Task 3): GET v1/facilities (cây cơ sở > khoa > phòng), POST …/branches|departments|rooms,
// PUT …/{branches|departments|rooms}/{id} kèm If-Match = rowVersion.
export const getFacilityTree = () => http.get('v1/facilities').then(response => response.data);
export const createBranch = ({ code, name }) => http.post('v1/facilities/branches', { code, name }).then(response => response.data);
export const createDepartment = ({
  branchId, code, name, kind,
}) => http.post('v1/facilities/departments', {
  branchId, code, name, kind,
}).then(response => response.data);
export const createRoom = ({ departmentId, code, name }) => http.post('v1/facilities/rooms', { departmentId, code, name }).then(response => response.data);
// type ∈ 'branches' | 'departments' | 'rooms'
export const updateFacility = (type, id, { name, isActive }, rowVersion) => http
  .put(`v1/facilities/${type}/${id}`, { name, isActive }, { headers: { 'If-Match': String(rowVersion) } })
  .then(response => response.data);
