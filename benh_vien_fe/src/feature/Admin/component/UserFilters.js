import React from 'react';
import PropTypes from 'prop-types';
import { SYSTEM_ROLES } from 'feature/Roles/permissionCatalog';

const STATUS_OPTIONS = [
  { value: '', label: 'Tất cả trạng thái' },
  { value: 'active', label: 'Hoạt động' },
  { value: 'must_change_password', label: 'Chờ đổi mật khẩu' },
  { value: 'locked', label: 'Đã khóa' },
];

const UserFilters = ({ value, onChange, onCreate, canCreate }) => (
  <div className="c-toolbar">
    <input
      className="c-toolbar__search"
      type="search"
      placeholder="Tìm theo tên hoặc email…"
      value={value.searchTerm}
      onChange={event => onChange({ ...value, searchTerm: event.target.value, pageNumber: 1 })}
      aria-label="Tìm tài khoản"
    />
    <select
      className="c-toolbar__select"
      value={value.roleId}
      onChange={event => onChange({ ...value, roleId: event.target.value, pageNumber: 1 })}
      aria-label="Lọc theo vai trò"
    >
      <option value="">Tất cả vai trò</option>
      {SYSTEM_ROLES.map(role => <option key={role.id} value={role.id}>{role.name}</option>)}
    </select>
    <select
      className="c-toolbar__select"
      value={value.status}
      onChange={event => onChange({ ...value, status: event.target.value, pageNumber: 1 })}
      aria-label="Lọc theo trạng thái"
    >
      {STATUS_OPTIONS.map(opt => <option key={opt.value} value={opt.value}>{opt.label}</option>)}
    </select>
    {canCreate && (
      <button type="button" className="c-toolbar__action" onClick={onCreate}>+ Tạo tài khoản</button>
    )}
  </div>
);

UserFilters.propTypes = {
  value: PropTypes.shape({ searchTerm: PropTypes.string, roleId: PropTypes.string, status: PropTypes.string }).isRequired,
  onChange: PropTypes.func.isRequired,
  onCreate: PropTypes.func.isRequired,
  canCreate: PropTypes.bool.isRequired,
};

export default UserFilters;
